#pragma once
#include <cmath>
#include "NpcTrackingPolicy.h"
#include "MovePlacement.h"

// Bounded registry of map NPCs and trainer spawns; all access runs on the game thread.
namespace TrainerSpawn {
using Byte=unsigned char;
struct Vec {float x,y,z;};
struct Rot {int pitch,yaw,roll;};
// Validated supported Engine.dll FCheckResult layout (also used by execFastTrace).
struct MoveCheck {void* next;void* actor;Vec location,normal;void* primitive;float time;int item;void* material;DWORD bone,level;};
static_assert(sizeof(MoveCheck)==0x38,"Supported x86 FCheckResult layout required");
static MoveCheck NewCheck(){MoveCheck c={};c.time=1;c.item=-1;return c;}
using LineCheckFn=int (__thiscall*)(void*,MoveCheck&,void*,const Vec&,const Vec&,DWORD,Vec,void*);
using RotateFn=int (__thiscall*)(void*,void*,Vec,Rot,MoveCheck&,int,int,int,int);
// The actor pointer BEFORE flags is the template; the actor pointer AFTER flags is the owner.
using SpawnFn=Byte* (__thiscall*)(void* level,void* actorClass,DWORD actorName,Vec location,Rot rotation,void* actorTemplate,int noCollisionFail,int remoteOwned,void* actorOwner,void* instigator);
using DestroyFn=int (__thiscall*)(void*,void*,int);
using EventFn=void (__thiscall*)(void*,void*,void*,void*);
using FindFn=void* (__thiscall*)(void*,DWORD,int);
using AddFn=int (__thiscall*)(void*,int,int);
struct SpawnRecord {DWORD id;Byte *pawn,*controller,*level;DWORD pawnIndex,controllerIndex,levelIndex,pawnName,controllerName;bool removing,spawned;};
struct Registry {DWORD version,nextId;SpawnRecord records[256];};
static Registry* registry=nullptr;
static HANDLE recordHandle=nullptr;
static bool SharedRecord() {
    if(registry)return registry->version==3;
    wchar_t key[96];swprintf_s(key,L"Local\\SCTrainer.SpawnRegistry.v3.%lu",GetCurrentProcessId());
    recordHandle=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Registry),key);
    if(!recordHandle)return false;
    bool exists=GetLastError()==ERROR_ALREADY_EXISTS;
    registry=static_cast<Registry*>(MapViewOfFile(recordHandle,FILE_MAP_ALL_ACCESS,0,0,sizeof(Registry)));
    if(!registry){CloseHandle(recordHandle);recordHandle=nullptr;return false;}
    if(!exists){
        ZeroMemory(registry,sizeof(Registry));registry->version=3;registry->nextId=1;
        // Import the previous trainer registry so existing spawned NPCs keep their origin.
        swprintf_s(key,L"Local\\SCTrainer.SpawnRegistry.v2.%lu",GetCurrentProcessId());
        auto previous=OpenFileMappingW(FILE_MAP_READ,FALSE,key);
        if(previous) {
            struct Legacy {DWORD version,nextId;SpawnRecord records[32];};
            auto old=static_cast<Legacy*>(MapViewOfFile(previous,FILE_MAP_READ,0,0,sizeof(Legacy)));
            if(old&&old->version==2){registry->nextId=old->nextId;for(int i=0;i<32;++i){registry->records[i]=old->records[i];registry->records[i].spawned=old->records[i].id!=0;}}
            if(old)UnmapViewOfFile(old);CloseHandle(previous);
        }
    }
    return registry->version==3;
}
static DWORD U(Byte* p,DWORD o){return *reinterpret_cast<DWORD*>(p+o);}
static DWORD Property(DWORD* objects,TrainerCheats::NameFn name,const wchar_t* owner,const wchar_t* wanted) {
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i) {
        auto obj=table[i];if(!obj||wcscmp(name(obj),wanted))continue;
        auto outer=reinterpret_cast<void*>(U(obj,0x18));
        if(!outer||wcscmp(name(outer),owner))continue;
        auto type=reinterpret_cast<void*>(U(obj,0x24));
        if(!type||!wcsstr(name(type),L"Property"))continue;
        auto offset=U(obj,0x44);
        if(offset>=0x28&&offset<0x8000)return offset;
    }
    return 0;
}
static const wchar_t* State(Byte* obj,TrainerCheats::NameFn name) {
    if(!obj)return L"None";
    // Supported Core.dll execGetStateName reads UObject+0x0c -> FStateFrame+0x1c.
    auto frame=reinterpret_cast<Byte*>(U(obj,0x0c));
    auto state=frame?reinterpret_cast<Byte*>(U(frame,0x1c)):nullptr;
    return state&&TrainerCheats::Alive(state)?name(state):L"None";
}
static bool Unconscious(const wchar_t* pawn,const wchar_t* ai) {
    return !wcscmp(pawn,L"s_Unconscious")||!wcscmp(pawn,L"s_InitiallyUnconscious")||
        !wcscmp(pawn,L"s_Electrocuted")||!wcscmp(ai,L"s_Unconscious");
}
static void* Function(Byte* actor,const wchar_t* wanted,DWORD* objects,TrainerCheats::NameFn name,FindFn find) {
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i) {
        auto obj=table[i];if(!obj||_wcsicmp(name(obj),wanted))continue;
        if(TrainerCheats::IsA(obj,L"Function"))return find(actor,U(obj,0x20),0);
    }
    return nullptr;
}
static bool Param(void* function,const wchar_t* wanted,DWORD offset,DWORD size,DWORD* objects,TrainerCheats::NameFn name) {
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i) {
        auto obj=table[i];if(!obj||reinterpret_cast<void*>(U(obj,0x18))!=function||_wcsicmp(name(obj),wanted))continue;
        return U(obj,0x44)==offset&&*reinterpret_cast<WORD*>(obj+0x3a)==size;
    }
    return false;
}
static void Condition(Request* r,SpawnRecord* record,int action,DWORD* objects,TrainerCheats::NameFn name,FindFn find,EventFn process) {
    auto pawn=record->pawn,ctrl=record->controller;
    DWORD hp=Property(objects,name,L"Pawn",L"Health"),pawnController=Property(objects,name,L"Pawn",L"Controller"),controllerPawn=Property(objects,name,L"Controller",L"Pawn");
    if(!hp||!pawnController||!controllerPawn){r->result=3;return;}
    if(record->removing||!TrainerCheats::Alive(ctrl,record->controllerIndex)||U(ctrl,0x20)!=record->controllerName||
        (U(ctrl,0x2c)&0x10)||reinterpret_cast<Byte*>(U(pawn,pawnController))!=ctrl||reinterpret_cast<Byte*>(U(ctrl,controllerPawn))!=pawn){r->result=35;return;}
    auto ps=State(pawn,name),ai=State(ctrl,name);
    bool unconscious=Unconscious(ps,ai);
    bool dead=!wcscmp(ps,L"s_Dying")||!wcscmp(ps,L"s_Dead")||!wcscmp(ai,L"s_Dead");
    if(dead){r->result=35;return;}
    if(action==1) {
        // The controller's state-specific Revive enters s_Groggy and handles recovery,
        // collision, AI group notification and the pawn's return to DefaultState.
        if(!unconscious||wcscmp(ai,L"s_Unconscious")){r->result=35;return;}
        auto revive=Function(ctrl,L"Revive",objects,name,find);
        if(!revive){r->result=3;return;}
        process(ctrl,revive,nullptr,nullptr);r->result=36;return;
    }
    if(!unconscious && wcscmp(ai,L"s_Default")&&wcscmp(ai,L"s_Investigate")&&wcscmp(ai,L"s_Alert")){r->result=35;return;}
    if(!wcscmp(ps,L"s_Carried")||!wcscmp(ps,L"s_Grabbed")){r->result=35;return;}
    if(action==2) {
        if(unconscious||static_cast<int>(U(pawn,hp))<=0){r->result=35;return;}
        auto knocked=Function(pawn,L"Knocked",objects,name,find);
        if(!knocked||!Param(knocked,L"PillTag",0,4,objects,name)||!Param(knocked,L"momentum",4,12,objects,name)){r->result=3;return;}
        struct {int pill;Vec momentum;} parameters={1,{0,0,0}};
        process(pawn,knocked,&parameters,nullptr);r->result=36;return;
    }
    if(action==3) {
        auto died=Function(pawn,L"DiedE",objects,name,find);
        DWORD keep=Property(objects,name,L"EPawn",L"bKeepNPCAlive"),mask=0;
        auto table=reinterpret_cast<Byte**>(objects[0]);
        for(DWORD i=0;i<objects[1];++i) {
            auto obj=table[i];if(obj&&!wcscmp(name(obj),L"bKeepNPCAlive")&&reinterpret_cast<void*>(U(obj,0x18))&&
                !wcscmp(name(reinterpret_cast<void*>(U(obj,0x18))),L"EPawn"))mask=U(obj,0x54);
        }
        if(!died||!keep||!mask||(mask&(mask-1))||!Param(died,L"Killer",0,4,objects,name)||
            !Param(died,L"PillTag",4,4,objects,name)||!Param(died,L"momentum",8,12,objects,name)){r->result=3;return;}
        DWORD original=U(pawn,keep);*reinterpret_cast<DWORD*>(pawn+keep)=original&~mask;
        *reinterpret_cast<int*>(pawn+hp)=0;
        struct {Byte* killer;int pill;Vec momentum;} parameters={nullptr,1,{0,0,0}};
        process(pawn,died,&parameters,nullptr);
        if(TrainerCheats::Alive(pawn,record->pawnIndex)&&U(pawn,0x20)==record->pawnName)
            *reinterpret_cast<DWORD*>(pawn+keep)=(U(pawn,keep)&~mask)|(original&mask);
        r->result=36;return;
    }
    r->result=6;
}
static int EnumValue(HMODULE core,DWORD* objects,TrainerCheats::NameFn name,const wchar_t* enumeration,const wchar_t* wanted) {
    auto text=reinterpret_cast<const wchar_t* (__thiscall*)(void*)>(GetProcAddress(core,"??DFName@@QBEPBGXZ"));
    if(!text)return -1;
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i) {
        auto obj=table[i];if(!obj||wcscmp(name(obj),enumeration)||!TrainerCheats::IsA(obj,L"Enum"))continue;
        // UEnum's FName array in the validated Core.dll.
        auto values=reinterpret_cast<DWORD*>(U(obj,0x38));DWORD count=U(obj,0x3c);
        if(!values||count>256)return -1;
        for(DWORD j=0;j<count;++j)if(!_wcsicmp(text(values+j),wanted))return static_cast<int>(j);
    }
    return -1;
}
static void Activity(Request* r,SpawnRecord* record,int activity,Byte* player,HMODULE core,DWORD* objects,TrainerCheats::NameFn name,FindFn find,EventFn process) {
    if(activity<1||activity>5){r->result=6;return;}
    auto pawn=record->pawn,ctrl=record->controller;
    DWORD hp=Property(objects,name,L"Pawn",L"Health"),pc=Property(objects,name,L"Pawn",L"Controller"),cp=Property(objects,name,L"Controller",L"Pawn");
    DWORD samOffset=Property(objects,name,L"EPlayerController",L"EPawn"),group=Property(objects,name,L"EAIController",L"Group");
    if(!hp||!pc||!cp||!samOffset||!group){r->result=3;return;}
    if(record->removing||static_cast<int>(U(pawn,hp))<=0||!TrainerCheats::Alive(ctrl,record->controllerIndex)||
       U(ctrl,0x20)!=record->controllerName||(U(ctrl,0x2c)&0x10)||reinterpret_cast<Byte*>(U(pawn,pc))!=ctrl||
       reinterpret_cast<Byte*>(U(ctrl,cp))!=pawn||!U(ctrl,group)){r->result=37;return;}
    auto ps=State(pawn,name),state=State(ctrl,name);
    if(Unconscious(ps,state)||(!wcscmp(ps,L"s_Grabbed"))||
       (wcscmp(state,L"s_Default")&&wcscmp(state,L"s_Investigate")&&wcscmp(state,L"s_Alert"))){r->result=37;return;}
    auto sam=reinterpret_cast<Byte*>(U(player,samOffset));
    if(!TrainerCheats::IsA(sam,L"EPawn")){r->result=16;return;}
    auto reset=Function(ctrl,L"ResetGoalList",objects,name,find);
    auto routes=Function(ctrl,L"ClearRoutes",objects,name,find);
    auto unlock=Function(ctrl,L"UnlockNavPoint",objects,name,find);
    auto patrol=Function(ctrl,L"RemoveReferencesInPatrolInfo",objects,name,find);
    auto replace=Function(ctrl,L"ReplaceDefaultGoal",objects,name,find);
    auto add=Function(ctrl,L"AddGoal",objects,name,find);
    auto goState=reinterpret_cast<int (__thiscall*)(void*,DWORD)>(GetProcAddress(core,"?GotoState@UObject@@UAE?AW4EGotoState@@VFName@@@Z"));
    int guard=EnumValue(core,objects,name,L"GoalType",L"GOAL_Guard");
    int move=EnumValue(core,objects,name,L"GoalType",activity==4?L"GOAL_QuickSearch":L"GOAL_MoveTo");
    int flags=EnumValue(core,objects,name,L"MoveFlags",activity==3?L"MOVE_JogAlert":activity==4?L"MOVE_Search":L"MOVE_WalkNormal");
    int wait=EnumValue(core,objects,name,L"MoveFlags",activity==5?L"MOVE_WalkAlert":L"MOVE_WalkNormal");
    DWORD stateName=0;const wchar_t* desired=activity==4?L"s_Investigate":activity==5?L"s_Alert":L"s_Default";
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i)if(table[i]&&!wcscmp(name(table[i]),desired)&&TrainerCheats::IsA(table[i],L"State"))stateName=U(table[i],0x20);
    if(!reset||!routes||!unlock||!patrol||!replace||!add||!goState||!stateName||guard<0||move<0||flags<0||wait<0){r->result=3;return;}
    // Verify every input layout before clearing an existing AI goal.
    for(auto fn:{replace,add}) {
        if(!Param(fn,L"_Type",0,1,objects,name)||!Param(fn,L"_Priority",1,1,objects,name)||
           !Param(fn,L"_Location",4,12,objects,name)||!Param(fn,L"_Focus",0x10,12,objects,name)||
           !Param(fn,L"_MoveFlags",0x34,1,objects,name)||!Param(fn,L"_WaitFlags",0x44,1,objects,name)||
           !Param(fn,L"_Value",0x30,4,objects,name)){r->result=3;return;}
    }
    Vec destination=*reinterpret_cast<Vec*>(pawn+0xd4);
    if(activity>=2&&activity<=4) {
        destination=*reinterpret_cast<Vec*>(sam+0xd4);
        float yaw=static_cast<int>(U(player,0xe4))*(6.28318530718f/65536.0f);
        destination.x+=std::cos(yaw)*400;destination.y+=std::sin(yaw)*400;
    }
    if(!std::isfinite(destination.x)||!std::isfinite(destination.y)||!std::isfinite(destination.z)){r->result=7;return;}
    process(ctrl,patrol,nullptr,nullptr);process(ctrl,unlock,nullptr,nullptr);process(ctrl,routes,nullptr,nullptr);
    process(ctrl,reset,nullptr,nullptr);
    alignas(4) Byte parameters[0x54]={};parameters[0]=static_cast<Byte>(guard);
    memcpy(parameters+4,&destination,sizeof(Vec));memcpy(parameters+0x10,&destination,sizeof(Vec));
    parameters[0x34]=static_cast<Byte>(wait);parameters[0x44]=static_cast<Byte>(wait);
    process(ctrl,replace,parameters,nullptr);
    if(activity>=2&&activity<=4) {
        ZeroMemory(parameters,sizeof(parameters));parameters[0]=static_cast<Byte>(move);parameters[1]=255;
        memcpy(parameters+4,&destination,sizeof(Vec));memcpy(parameters+0x10,&destination,sizeof(Vec));
        parameters[0x34]=static_cast<Byte>(flags);parameters[0x44]=static_cast<Byte>(wait);
        float arrivalRadius=50;memcpy(parameters+0x30,&arrivalRadius,4);
        process(ctrl,add,parameters,nullptr);
    }
    goState(ctrl,stateName);
    r->result=38;
}
static SpawnRecord* AvailableRecord() {
    for(auto& entry:registry->records)if(!entry.id)return &entry;
    SpawnRecord* candidate=nullptr;
    for(auto& entry:registry->records)if(!entry.pawn&&(!candidate||entry.id<candidate->id))candidate=&entry;
    return candidate;
}
static void DiscoverMap(Byte* level,Byte* info,DWORD* objects,TrainerCheats::NameFn name) {
    if(!level||!info)return;
    DWORD controllerOffset=Property(objects,name,L"Pawn",L"Controller");
    if(!controllerOffset)return;
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i) {
        auto pawn=table[i];if(!pawn||!TrainerCheats::IsA(pawn,L"EAIPawn")||(U(pawn,0x2c)&0x10)||reinterpret_cast<Byte*>(U(pawn,0x6c))!=info)continue;
        bool known=false;
        for(auto& entry:registry->records)if(entry.pawn==pawn&&entry.pawnIndex==i&&entry.pawnName==U(pawn,0x20)){known=true;break;}
        if(known)continue;
        auto entry=AvailableRecord();if(!entry)break;
        *entry={};entry->id=registry->nextId++;entry->pawn=pawn;entry->pawnIndex=i;entry->pawnName=U(pawn,0x20);
        entry->level=level;entry->levelIndex=U(level,4);
        auto ctrl=reinterpret_cast<Byte*>(U(pawn,controllerOffset));
        if(TrainerCheats::IsA(ctrl,L"EAIController")&&!(U(ctrl,0x2c)&0x10)){entry->controller=ctrl;entry->controllerIndex=U(ctrl,4);entry->controllerName=U(ctrl,0x20);}
    }
}
// Revival and mission scripts can replace a controller without replacing the pawn.
// Keep the stable pawn record, but refresh its current reciprocal controller link.
static void RefreshControllers(DWORD* objects,TrainerCheats::NameFn name) {
    DWORD pc=Property(objects,name,L"Pawn",L"Controller"),cp=Property(objects,name,L"Controller",L"Pawn");
    if(!pc||!cp)return;
    for(auto& item:registry->records){
        if(!item.pawn||item.removing)continue;
        auto ctrl=reinterpret_cast<Byte*>(U(item.pawn,pc));
        bool valid=TrainerCheats::IsA(ctrl,L"EAIController");
        bool bind=NpcTrackingPolicy::CanBind(valid,valid&&U(ctrl,0x6c)==U(item.pawn,0x6c),
            valid&&(U(ctrl,0x2c)&0x10)!=0,valid?U(ctrl,cp):0,reinterpret_cast<DWORD>(item.pawn));
        item.controller=bind?ctrl:nullptr;
        item.controllerIndex=bind?U(ctrl,4):0;item.controllerName=bind?U(ctrl,0x20):0;
    }
}
static void Position(Request* r,SpawnRecord* record,int mode,Vec destination,Byte* player,HMODULE engine,DWORD* objects,TrainerCheats::NameFn name,int metres=3,int slot=0,bool probe=false) {
    auto pawn=record->pawn,ctrl=record->controller;
    DWORD hp=Property(objects,name,L"Pawn",L"Health"),pc=Property(objects,name,L"Pawn",L"Controller"),cp=Property(objects,name,L"Controller",L"Pawn");
    if(!hp||!pc||!cp){r->result=3;return;}
    if(record->removing||static_cast<int>(U(pawn,hp))<=0||!TrainerCheats::Alive(ctrl,record->controllerIndex)||
       U(ctrl,0x20)!=record->controllerName||(U(ctrl,0x2c)&0x10)||reinterpret_cast<Byte*>(U(pawn,pc))!=ctrl||reinterpret_cast<Byte*>(U(ctrl,cp))!=pawn){r->result=37;return;}
    auto state=State(ctrl,name),ps=State(pawn,name);
    if(Unconscious(ps,state)||(wcscmp(state,L"s_Default")&&wcscmp(state,L"s_Investigate")&&wcscmp(state,L"s_Alert"))){r->result=37;return;}
    Vec original=*reinterpret_cast<Vec*>(pawn+0xd4),samLocation={};float facingYaw=0;DWORD traceClear=1;
    auto line=reinterpret_cast<LineCheckFn>(GetProcAddress(engine,"?SingleLineCheck@ULevel@@UAEHAAUFCheckResult@@PAVAActor@@ABVFVector@@2KV4@1@Z"));
    auto rotate=reinterpret_cast<RotateFn>(GetProcAddress(engine,"?MoveActor@ULevel@@UAEHPAVAActor@@VFVector@@VFRotator@@AAUFCheckResult@@HHHH@Z"));
    if(mode==1&&!MovePlacement::Valid(metres,slot)){r->result=6;return;}
    if(mode) {
        DWORD samOffset=Property(objects,name,L"EPlayerController",L"EPawn"),height=Property(objects,name,L"Actor",L"CollisionHeight");
        if(!samOffset||!height){r->result=3;return;}
        auto sam=reinterpret_cast<Byte*>(U(player,samOffset));if(!TrainerCheats::IsA(sam,L"EPawn")){r->result=16;return;}
        destination=*reinterpret_cast<Vec*>(sam+0xd4);samLocation=destination;
        destination.z+=*reinterpret_cast<float*>(pawn+height)-*reinterpret_cast<float*>(sam+height);
        if(mode==1){
            facingYaw=static_cast<int>(U(player,0xe4))*(6.28318530718f/65536.0f);
            auto delta=MovePlacement::Offset(metres,slot,facingYaw);destination.x+=delta.x;destination.y+=delta.y;
            if(!line||!rotate){r->result=60;return;}
            // Call the validated native collision API directly. These indexed native
            // functions are not dispatched by UObject::ProcessEvent.
            bool grounded=false;float floorZ=0;MoveCheck last=NewCheck();
            for(float lift:{150.0f,300.0f,75.0f,0.0f}){
                Vec start={destination.x,destination.y,samLocation.z+lift},end={destination.x,destination.y,samLocation.z-400};
                last=NewCheck();line(record->level,last,sam,end,start,0xd7,{0,0,0},nullptr);
                if(last.actor&&last.normal.z>=0.5f&&std::isfinite(last.location.z)&&std::fabs(last.location.z-samLocation.z)<400){floorZ=last.location.z;grounded=true;break;}
            }
            if(!grounded){
                if(probe){swprintf_s(r->map,L"GROUND actor=%08lX normal=%.1f,%.1f,%.1f hit=%.0f,%.0f,%.0f",reinterpret_cast<DWORD>(last.actor),last.normal.x,last.normal.y,last.normal.z,last.location.x,last.location.y,last.location.z);r->result=61;return;}
                r->result=62;return;
            }
            float npcHeight=*reinterpret_cast<float*>(pawn+height),samHeight=*reinterpret_cast<float*>(sam+height);
            if(!std::isfinite(npcHeight)||npcHeight<=0||npcHeight>300||!std::isfinite(samHeight)||samHeight<=0||samHeight>300){r->result=60;return;}
            destination.z=floorZ+npcHeight+2;
            Vec from=samLocation,to=destination;from.z+=samHeight*0.8f;to.z+=npcHeight*0.8f;
            auto sight=NewCheck();line(record->level,sight,sam,to,from,0xd7,{0,0,0},nullptr);traceClear=sight.actor?0:1;
            if(!traceClear&&!probe){r->result=63;return;}
        }
    }
    for(float value:{destination.x,destination.y,destination.z})if(!std::isfinite(value)||std::fabs(value)>1000000){r->result=6;return;}
    // Exactly the normal Actor.SetLocation path: FarMoveActor with all three
    // optional flags zero. This preserves collision checks and volume updates.
    auto move=reinterpret_cast<int (__thiscall*)(void*,void*,Vec,int,int,int)>(GetProcAddress(engine,"?FarMoveActor@ULevel@@UAEHPAVAActor@@VFVector@@HHH@Z"));
    if(!move||reinterpret_cast<Byte*>(U(pawn,0x70))!=record->level){r->result=3;return;}
    if(probe){
        int fits=traceClear?move(record->level,pawn,destination,1,0,0):0;
        swprintf_s(r->map,L"FRONT trace=%lu fits=%d yaw=%.0f Sam=%.0f,%.0f,%.0f To=%.0f,%.0f,%.0f",traceClear,fits,facingYaw*57.29578f,samLocation.x,samLocation.y,samLocation.z,destination.x,destination.y,destination.z);
        r->result=61;return;
    }
    if(!move(record->level,pawn,destination,0,0,0)){r->result=40;return;}
    auto actual=*reinterpret_cast<Vec*>(pawn+0xd4);
    if(mode==1){
        if(!MovePlacement::FarEnough(actual.x-samLocation.x,actual.y-samLocation.y,facingYaw,metres)){
            move(record->level,pawn,original,0,0,0);r->result=40;return;
        }
        Rot rotation={0,static_cast<int>(std::atan2(actual.y-samLocation.y,actual.x-samLocation.x)*(65536.0f/6.28318530718f)),0};
        auto turn=NewCheck();rotate(record->level,pawn,{0,0,0},rotation,turn,0,0,0,0);
        turn=NewCheck();rotate(record->level,ctrl,{0,0,0},rotation,turn,0,0,0,0);
    }
    swprintf_s(r->map,L"NPC %lu position: %.2f, %.2f, %.2f",record->id,actual.x,actual.y,actual.z);
    r->result=41;
}
static void Snapshot(Request* r,DWORD* objects,TrainerCheats::NameFn name,Byte* player) {
    wchar_t key[96];NpcSnapshotName(key,GetCurrentProcessId());
    auto handle=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,key);
    if(!handle){r->result=7;return;}
    auto output=static_cast<NpcSnapshot*>(MapViewOfFile(handle,FILE_MAP_ALL_ACCESS,0,0,sizeof(NpcSnapshot)));
    if(!output){CloseHandle(handle);r->result=7;return;}
    ZeroMemory(output,sizeof(NpcSnapshot));output->version=3;
    DWORD hp=Property(objects,name,L"Pawn",L"Health"),vel=Property(objects,name,L"Actor",L"Velocity");
    DWORD pawnOffset=Property(objects,name,L"EPlayerController",L"EPawn");
    DWORD movement=Property(objects,name,L"EPawn",L"BaseMoveFlags");
    DWORD weapon=Property(objects,name,L"EPawn",L"CurrentWeapon");
    DWORD targetOffset=Property(objects,name,L"EAIController",L"TargetActorToFire");
    DWORD protectOffset=0,protectMask=0,hostileOffset=0,hostileMask=0;
    auto objectTable=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i){
        auto obj=objectTable[i];if(!obj||!TrainerCheats::IsA(obj,L"BoolProperty"))continue;
        auto owner=reinterpret_cast<Byte*>(U(obj,0x18));if(!TrainerCheats::Alive(owner)||wcscmp(name(owner),L"EPawn"))continue;
        if(!wcscmp(name(obj),L"bKeepNPCAlive")){protectOffset=U(obj,0x44);protectMask=U(obj,0x54);}
        if(!wcscmp(name(obj),L"bHostile")){hostileOffset=U(obj,0x44);hostileMask=U(obj,0x54);}
    }
    auto sam=player&&pawnOffset?reinterpret_cast<Byte*>(U(player,pawnOffset)):nullptr;
    for(auto& item:registry->records) {
        if(!item.id)continue;
        auto& n=output->entries[output->count++];n.id=item.id;n.spawned=item.spawned;
        wcscpy_s(n.pawnState,L"None");wcscpy_s(n.aiState,L"None");wcscpy_s(n.weapon,L"None");wcscpy_s(n.target,L"None");
        if(!item.pawn){n.present=0;continue;}
        n.present=1;n.removing=item.removing;
        // Unknown metadata is protected conservatively; spawned guards remain included.
        n.protectedGlobal=!item.spawned&&(!protectOffset||!protectMask||!hostileOffset||!hostileMask||
            (U(item.pawn,protectOffset)&protectMask)||!(U(item.pawn,hostileOffset)&hostileMask));
        auto position=*reinterpret_cast<Vec*>(item.pawn+0xd4);n.x=position.x;n.y=position.y;n.z=position.z;
        wcsncpy_s(n.actorName,name(item.pawn),_TRUNCATE);
        n.moveFlags=movement?*(item.pawn+movement):0;
        n.health=hp?static_cast<int>(U(item.pawn,hp)):-1;
        wcsncpy_s(n.type,name(reinterpret_cast<void*>(U(item.pawn,0x24))),_TRUNCATE);
        wcsncpy_s(n.pawnState,State(item.pawn,name),_TRUNCATE);
        auto ctrl=item.controller;
        bool controller=TrainerCheats::Alive(ctrl,item.controllerIndex)&&U(ctrl,0x20)==item.controllerName&&!(U(ctrl,0x2c)&0x10);
        n.controller=controller;
        if(controller)wcsncpy_s(n.aiState,State(ctrl,name),_TRUNCATE);
        if(controller&&targetOffset) {
            auto target=reinterpret_cast<Byte*>(U(ctrl,targetOffset));
            if(TrainerCheats::Alive(target)&&!(U(target,0x2c)&0x10))wcsncpy_s(n.target,target==sam?L"Sam Fisher":name(target),_TRUNCATE);
        }
        if(weapon) {
            auto w=reinterpret_cast<Byte*>(U(item.pawn,weapon));
            if(TrainerCheats::Alive(w))wcsncpy_s(n.weapon,name(reinterpret_cast<void*>(U(w,0x24))),_TRUNCATE);
        }
        if(vel){auto v=*reinterpret_cast<Vec*>(item.pawn+vel);n.speed=std::sqrt(v.x*v.x+v.y*v.y);}
        if(TrainerCheats::Alive(sam)) {
            auto a=*reinterpret_cast<Vec*>(item.pawn+0xd4),b=*reinterpret_cast<Vec*>(sam+0xd4);
            n.distance=std::sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z))/100.0f;
        }
    }
    UnmapViewOfFile(output);CloseHandle(handle);r->result=32;
}
static bool Process(Request* r,void* view,HMODULE core,HMODULE engine,DWORD* objects) {
    bool list=!wcscmp(r->map,L"--npc-list");
    int freezeValue=0;
    bool spawnRequest=!wcscmp(r->map,L"--npc-spawn");
    DWORD id=0;Vec destination={};int desiredHealth=0,desiredCondition=0,desiredActivity=0;wchar_t extra=0;
    bool freeze=swscanf_s(r->map,L"--npc-freeze:%lu:%d%c",&id,&freezeValue,&extra,1)==2;
    bool remove=swscanf_s(r->map,L"--npc-remove:%lu%c",&id,&extra,1)==1;
    bool heal=swscanf_s(r->map,L"--npc-health:%lu:%d%c",&id,&desiredHealth,&extra,1)==2;
    bool condition=swscanf_s(r->map,L"--npc-condition:%lu:%d%c",&id,&desiredCondition,&extra,1)==2;
    bool activity=swscanf_s(r->map,L"--npc-activity:%lu:%d%c",&id,&desiredActivity,&extra,1)==2;
    bool coordinates=swscanf_s(r->map,L"--npc-position:%lu:%f:%f:%f%c",&id,&destination.x,&destination.y,&destination.z,&extra,1)==4;
    int frontMetres=3,frontSlot=0;
    bool frontProbe=swscanf_s(r->map,L"--npc-front-probe:%lu:%d:%d%c",&id,&frontMetres,&frontSlot,&extra,1)==3;
    bool front=frontProbe||swscanf_s(r->map,L"--npc-front:%lu:%d:%d%c",&id,&frontMetres,&frontSlot,&extra,1)==3;
    if(!front){frontMetres=3;frontSlot=0;front=swscanf_s(r->map,L"--npc-front:%lu%c",&id,&extra,1)==1;}
    if(front&&!MovePlacement::Valid(frontMetres,frontSlot)){r->result=6;return true;}
    bool toSam=swscanf_s(r->map,L"--npc-to-sam:%lu%c",&id,&extra,1)==1;
    bool position=coordinates||front||toSam;
    if(!list&&!spawnRequest&&!freeze&&!remove&&!heal&&!condition&&!activity&&!position)return false;
    if((freeze||remove||heal||condition||activity||position)&&!id){r->result=6;return true;}
    if(!SharedRecord()){r->result=7;return true;}
    auto name=reinterpret_cast<TrainerCheats::NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));
    auto spawn=reinterpret_cast<SpawnFn>(GetProcAddress(engine,"?SpawnActor@ULevel@@UAEPAVAActor@@PAVUClass@@VFName@@VFVector@@VFRotator@@PAV2@HH4PAVAPawn@@@Z"));
    auto destroy=reinterpret_cast<DestroyFn>(GetProcAddress(engine,"?DestroyActor@ULevel@@UAEHPAVAActor@@H@Z"));
    auto process=reinterpret_cast<EventFn>(GetProcAddress(core,"?ProcessEvent@UObject@@UAEXPAVUFunction@@PAX1@Z"));
    auto find=reinterpret_cast<FindFn>(GetProcAddress(core,"?FindFunction@UObject@@QAEPAVUFunction@@VFName@@H@Z"));
    auto add=reinterpret_cast<AddFn>(GetProcAddress(core,"?Add@FArray@@QAEHHH@Z"));
    if(!name||!spawn||!destroy||!process||!find||!add){r->result=3;return true;}
    TrainerCheats::objectArray=objects;TrainerCheats::name=name;
    auto table=reinterpret_cast<Byte**>(objects[0]);DWORD count=objects[1];
    if(count>1000000){r->result=4;return true;}
    auto player=reinterpret_cast<Byte*>(U(static_cast<Byte*>(view),0x34));
    auto info=player?reinterpret_cast<Byte*>(U(player,0x6c)):nullptr;
    auto package=info?reinterpret_cast<Byte*>(U(info,0x18)):nullptr;
    Byte* level=nullptr;
    DWORD possessName=0;
    bool mission=TrainerCheats::IsA(player,L"EPlayerController")&&package&&_wcsicmp(name(package),L"menu");
    for(DWORD i=0;i<count;++i) {
        auto obj=table[i];if(!obj)continue;
        auto cls=reinterpret_cast<void*>(U(obj,0x24));
        if(mission&&cls&&!wcscmp(name(cls),L"Level")&&reinterpret_cast<Byte*>(U(obj,0x18))==package)level=obj;
        if(!wcscmp(name(obj),L"Possess")&&cls&&!wcscmp(name(cls),L"Function"))possessName=U(obj,0x20);
    }
    for(auto& item:registry->records) {
        if(item.pawn && (!level||item.level!=level||!TrainerCheats::Alive(item.level,item.levelIndex)||
          !TrainerCheats::Alive(item.pawn,item.pawnIndex)||U(item.pawn,0x20)!=item.pawnName||(U(item.pawn,0x2c)&0x10))) {
            item.pawn=nullptr;item.controller=nullptr;item.removing=false;
        }
    }
    DiscoverMap(level,info,objects,name);
    RefreshControllers(objects,name);
    if(list){Snapshot(r,objects,name,player);return true;}
    if(!level||!possessName){r->result=16;return true;}
    SpawnRecord* record=nullptr;
    if(freeze||remove||heal||condition||activity||position) {
        for(auto& item:registry->records)if(item.id==id)record=&item;
        if(!record||!record->pawn){r->result=22;return true;}
    }
    if(!frontProbe&&(GetForegroundWindow()!=r->window || IsIconic(r->window))){r->result=14;return true;}
    if(freeze){
        if((freezeValue!=0&&freezeValue!=1)||record->removing){r->result=6;return true;}
        r->result=TrainerFreeze::Set(record->pawn,freezeValue==1,r->window,static_cast<Byte*>(view))?59:57;return true;
    }
    if(position){Position(r,record,front?1:toSam?2:0,destination,player,engine,objects,name,frontMetres,frontSlot,frontProbe);return true;}
    if(activity){Activity(r,record,desiredActivity,player,core,objects,name,find,process);return true;}
    if(condition){Condition(r,record,desiredCondition,objects,name,find,process);return true;}
    if(remove) {
        if(!record->spawned){r->result=39;return true;}
        if(record->removing){r->result=28;return true;}
        DWORD lifeSpan=Property(objects,name,L"Actor",L"LifeSpan");
        if(!lifeSpan){r->result=3;return true;}
        // Do not call DestroyActor from the window hook. Expire the pawn during the
        // engine's normal actor tick. First perform the game's normal AI death cleanup;
        // generic Pawn.Destroyed alone skips AI-group, goal and patrol references.
        auto ctrl=record->controller;
        if(TrainerCheats::Alive(ctrl,record->controllerIndex) && U(ctrl,0x20)==record->controllerName && !(U(ctrl,0x2c)&0x10)) {
            auto goState=reinterpret_cast<int (__thiscall*)(void*,DWORD)>(GetProcAddress(core,"?GotoState@UObject@@UAE?AW4EGotoState@@VFName@@@Z"));
            DWORD deadState=0,removePatrolName=0;
            for(DWORD i=0;i<count;++i) {
                auto obj=table[i];if(!obj)continue;
                if(!wcscmp(name(obj),L"s_Dead"))deadState=U(obj,0x20);
                if(!wcscmp(name(obj),L"RemoveReferencesInPatrolInfo"))removePatrolName=U(obj,0x20);
            }
            DWORD health=Property(objects,name,L"Pawn",L"Health");
            DWORD group=Property(objects,name,L"EAIController",L"Group");
            if(!goState||!deadState||!removePatrolName||!health||!group){r->result=3;return true;}
            auto removePatrol=find(ctrl,removePatrolName,0);
            if(!removePatrol){r->result=3;return true;}
            *reinterpret_cast<int*>(record->pawn+health)=0;
            goState(ctrl,deadState);
            process(ctrl,removePatrol,nullptr,nullptr);
            if(U(ctrl,group)!=0){r->result=30;return true;}
        }
        *reinterpret_cast<float*>(record->pawn+lifeSpan)=0.1f;
        record->removing=true;r->result=28;return true;
    }
    if(heal) {
        if(desiredHealth<1||desiredHealth>100||record->removing){r->result=6;return true;}
        DWORD health=Property(objects,name,L"Pawn",L"Health");
        if(!health){r->result=3;return true;}
        // A health edit never revives a dead or unconscious actor.
        auto pawnState=State(record->pawn,name);
        auto aiState=TrainerCheats::Alive(record->controller,record->controllerIndex)?State(record->controller,name):L"None";
        if(static_cast<int>(U(record->pawn,health))<=0||wcsstr(pawnState,L"Unconscious")||!wcscmp(pawnState,L"s_Electrocuted")||!wcscmp(aiState,L"s_Unconscious")||!wcscmp(aiState,L"s_Dead")){r->result=33;return true;}
        *reinterpret_cast<int*>(record->pawn+health)=desiredHealth;r->result=34;return true;
    }
    int active=0;
    for(auto& item:registry->records)if(item.pawn&&item.spawned)++active;
    if(active>=8){r->result=23;return true;}
    record=AvailableRecord();
    if(!record){r->result=42;return true;}
    DWORD pawnOffset=Property(objects,name,L"EPlayerController",L"EPawn");
    DWORD invOffset=Property(objects,name,L"EPawn",L"FullInventory");
    DWORD listOffset=Property(objects,name,L"EInventory",L"InventoryList");
    DWORD initialOffset=Property(objects,name,L"EPawn",L"DynInitialInventory");
    DWORD controllerOffset=Property(objects,name,L"Pawn",L"Controller");
    DWORD healthOffset=Property(objects,name,L"Pawn",L"Health");
    DWORD hostileOffset=Property(objects,name,L"EPawn",L"bHostile");
    if(!pawnOffset||!invOffset||!listOffset||!initialOffset||!controllerOffset||!healthOffset||!hostileOffset){r->result=3;return true;}
    auto sam=reinterpret_cast<Byte*>(U(player,pawnOffset));
    if(!TrainerCheats::IsA(sam,L"EPawn")){r->result=16;return true;}
    Vec location=*reinterpret_cast<Vec*>(sam+0xd4);
    Rot rotation=*reinterpret_cast<Rot*>(player+0xe0);rotation.pitch=0;rotation.roll=0;
    Byte *source=nullptr,*weaponClass=nullptr,*controllerClass=nullptr;
    float closest=3.4e38f;
    for(DWORD i=0;i<count;++i) {
        auto p=table[i];if(!TrainerCheats::IsA(p,L"EAIProfessional"))continue;
        if((U(p,0x2c)&0x10)||U(p,0x6c)!=reinterpret_cast<DWORD>(info)||static_cast<int>(U(p,healthOffset))<=0||!(U(p,hostileOffset)&1))continue;
        auto ctrl=reinterpret_cast<Byte*>(U(p,controllerOffset));if(!TrainerCheats::IsA(ctrl,L"EAIController"))continue;
        auto inv=reinterpret_cast<Byte*>(U(p,invOffset));if(!TrainerCheats::IsA(inv,L"EInventory"))continue;
        auto cats=*reinterpret_cast<TrainerCheats::Array*>(inv+listOffset);
        if(cats.count<0||cats.count>16||cats.count>cats.capacity||!cats.data)continue;
        Byte* weapon=nullptr;
        for(int c=0;c<cats.count&&!weapon;++c) {
            auto items=reinterpret_cast<TrainerCheats::Array*>(cats.data)[c];
            if(items.count<0||items.count>128||items.count>items.capacity||!items.data)continue;
            for(int j=0;j<items.count;++j) {
                auto item=reinterpret_cast<Byte**>(items.data)[j];
                if(TrainerCheats::IsA(item,L"EWeapon")){weapon=item;break;}
            }
        }
        if(!weapon)continue;
        Vec pLoc=*reinterpret_cast<Vec*>(p+0xd4);
        float dx=pLoc.x-location.x,dy=pLoc.y-location.y,dz=pLoc.z-location.z;
        float distance=dx*dx+dy*dy+dz*dz;
        if(distance<closest){closest=distance;source=p;weaponClass=reinterpret_cast<Byte*>(U(weapon,0x24));controllerClass=reinterpret_cast<Byte*>(U(ctrl,0x24));}
    }
    if(!source){r->result=24;return true;}
    Byte* created=nullptr;
    static const float angles[]={0,0.523599f,-0.523599f};
    for(float distance:{160.0f,240.0f}) {
        for(float offset:angles) {
            float angle=rotation.yaw*(6.28318530718f/65536.0f)+offset;
            Vec candidate={location.x+std::cos(angle)*distance,location.y+std::sin(angle)*distance,location.z+10};
            created=spawn(level,reinterpret_cast<void*>(U(source,0x24)),0,candidate,rotation,nullptr,0,0,nullptr,nullptr);
            if(created)break;
        }
        if(created)break;
    }
    if(!created){r->result=25;return true;}
    *record={};record->id=registry->nextId++;record->spawned=true;
    record->level=level;record->levelIndex=U(level,4);record->pawn=created;record->pawnIndex=U(created,4);record->pawnName=U(created,0x20);record->controller=nullptr;
    // Use engine allocation so reflected class arrays have normal ownership and GC behaviour.
    auto inventory=reinterpret_cast<TrainerCheats::Array*>(created+initialOffset);
    if(inventory->count==0) {
        int index=add(inventory,1,sizeof(void*));
        reinterpret_cast<Byte**>(inventory->data)[index]=weaponClass;
    }
    Vec at=*reinterpret_cast<Vec*>(created+0xd4);
    auto ctrl=spawn(level,controllerClass,0,at,rotation,nullptr,0,0,nullptr,nullptr);
    if(!ctrl){destroy(level,created,0);record->pawn=nullptr;r->result=26;return true;}
    record->controller=ctrl;record->controllerIndex=U(ctrl,4);record->controllerName=U(ctrl,0x20);
    auto possess=find(ctrl,possessName,0);
    if(!possess){destroy(level,created,0);if(TrainerCheats::Alive(ctrl,record->controllerIndex))destroy(level,ctrl,0);record->pawn=nullptr;record->controller=nullptr;r->result=26;return true;}
    struct {Byte* pawn;} parameters={created};
    process(ctrl,possess,&parameters,nullptr);
    if(reinterpret_cast<Byte*>(U(created,controllerOffset))!=ctrl) {
        destroy(level,created,0);if(TrainerCheats::Alive(ctrl,record->controllerIndex))destroy(level,ctrl,0);
        record->pawn=nullptr;record->controller=nullptr;r->result=26;return true;
    }
    swprintf_s(r->map,L"NPC %lu: %s",record->id,name(reinterpret_cast<void*>(U(created,0x24))));
    r->result=21;return true;
}
static bool SafeRequest(Request* r,void* view,HMODULE core,HMODULE engine,DWORD* objects) {
    try{return Process(r,view,core,engine,objects);}
    catch(...){r->result=7;return true;}
}
}
