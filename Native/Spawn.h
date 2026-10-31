#pragma once
#include <cmath>

// Bounded registry of trainer-created NPCs; all access runs on the game thread.
namespace TrainerSpawn {
using Byte=unsigned char;
struct Vec {float x,y,z;};
struct Rot {int pitch,yaw,roll;};
using SpawnFn=Byte* (__thiscall*)(void*,void*,DWORD,Vec,Rot,void*,int,int,void*,void*);
using DestroyFn=int (__thiscall*)(void*,void*,int);
using EventFn=void (__thiscall*)(void*,void*,void*,void*);
using FindFn=void* (__thiscall*)(void*,DWORD,int);
using AddFn=int (__thiscall*)(void*,int,int);
struct SpawnRecord {DWORD id;Byte *pawn,*controller,*level;DWORD pawnIndex,controllerIndex,levelIndex,pawnName,controllerName;bool removing;};
struct Registry {DWORD version,nextId;SpawnRecord records[32];};
static Registry* registry=nullptr;
static HANDLE recordHandle=nullptr;
static bool SharedRecord() {
    if(registry)return registry->version==2;
    wchar_t key[96];swprintf_s(key,L"Local\\SCTrainer.SpawnRegistry.v2.%lu",GetCurrentProcessId());
    recordHandle=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Registry),key);
    if(!recordHandle)return false;
    bool exists=GetLastError()==ERROR_ALREADY_EXISTS;
    registry=static_cast<Registry*>(MapViewOfFile(recordHandle,FILE_MAP_ALL_ACCESS,0,0,sizeof(Registry)));
    if(!registry){CloseHandle(recordHandle);recordHandle=nullptr;return false;}
    if(!exists){ZeroMemory(registry,sizeof(Registry));registry->version=2;registry->nextId=1;}
    return registry->version==2;
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
static void Snapshot(Request* r,DWORD* objects,TrainerCheats::NameFn name,Byte* player) {
    wchar_t key[96];NpcSnapshotName(key,GetCurrentProcessId());
    auto handle=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,key);
    if(!handle){r->result=7;return;}
    auto output=static_cast<NpcSnapshot*>(MapViewOfFile(handle,FILE_MAP_ALL_ACCESS,0,0,sizeof(NpcSnapshot)));
    if(!output){CloseHandle(handle);r->result=7;return;}
    ZeroMemory(output,sizeof(NpcSnapshot));output->version=1;
    DWORD hp=Property(objects,name,L"Pawn",L"Health"),vel=Property(objects,name,L"Actor",L"Velocity");
    DWORD pawnOffset=Property(objects,name,L"EPlayerController",L"EPawn");
    DWORD movement=Property(objects,name,L"EPawn",L"BaseMoveFlags");
    DWORD weapon=Property(objects,name,L"EPawn",L"CurrentWeapon");
    DWORD targetOffset=Property(objects,name,L"EAIController",L"TargetActorToFire");
    auto sam=player&&pawnOffset?reinterpret_cast<Byte*>(U(player,pawnOffset)):nullptr;
    for(auto& item:registry->records) {
        if(!item.id)continue;
        auto& n=output->entries[output->count++];n.id=item.id;
        wcscpy_s(n.pawnState,L"None");wcscpy_s(n.aiState,L"None");wcscpy_s(n.weapon,L"None");wcscpy_s(n.target,L"None");
        if(!item.pawn){n.present=0;continue;}
        n.present=1;n.removing=item.removing;
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
    bool spawnRequest=!wcscmp(r->map,L"--npc-spawn");
    DWORD id=0;int desiredHealth=0;wchar_t extra=0;
    bool remove=swscanf_s(r->map,L"--npc-remove:%lu%c",&id,&extra,1)==1;
    bool heal=swscanf_s(r->map,L"--npc-health:%lu:%d%c",&id,&desiredHealth,&extra,1)==2;
    if(!list&&!spawnRequest&&!remove&&!heal)return false;
    if((remove||heal)&&!id){r->result=6;return true;}
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
    if(list){Snapshot(r,objects,name,player);return true;}
    if(!level||!possessName){r->result=16;return true;}
    SpawnRecord* record=nullptr;
    if(remove||heal) {
        for(auto& item:registry->records)if(item.id==id)record=&item;
        if(!record||!record->pawn){r->result=22;return true;}
    }
    if(GetForegroundWindow()!=r->window || IsIconic(r->window)){r->result=14;return true;}
    if(remove) {
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
    for(auto& item:registry->records)if(item.pawn)++active;
    if(active>=8){r->result=23;return true;}
    for(auto& item:registry->records)if(!item.id){record=&item;break;}
    if(!record)for(auto& item:registry->records)if(!item.pawn&&(!record||item.id<record->id))record=&item;
    if(!record){r->result=23;return true;}
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
    *record={};record->id=registry->nextId++;
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
