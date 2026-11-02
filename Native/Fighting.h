#pragma once
#include "FightPolicy.h"
// Bounded game-thread combat session; all actor identities are revalidated on each tick.
namespace TrainerFighting {
using namespace TrainerCheats;
using TrainerSpawn::Property;
struct Record {Byte* pawn;Byte* controller;DWORD index,controllerIndex;void* add;};
static Record participants[256]={};
static int participantCount=0,attackType=0,guardType=0,moveFlags=0;
static DWORD healthOffset=0,controlOffset=0,pawnOffset=0,goalListOffset=0,headOffset=0,goalTypeOffset=0,goalTargetOffset=0,
    goalFocusOffset=0,goalUpdateOffset=0,goalUpdateMask=0,fireTargetOffset=0,enemyOffset=0,seenOffset=0,seenMask=0,alertState=0,shootOffset=0,shootMask=0,initializedOffset=0,initializedMask=0,goalLocationOffset=0;
static Byte *sessionLevel=nullptr,*sessionViewport=nullptr,*sessionPlayer=nullptr;static DWORD levelIndex=0,sessionPlayerIndex=0,sessionViewportIndex=0;
static HWND sessionWindow=nullptr;static UINT_PTR sessionTimer=0;static ULONGLONG deadline=0;
static TrainerSpawn::EventFn dispatch=nullptr;
static int (__thiscall* gotoState)(void*,DWORD)=nullptr;
static bool Live(const Record& r) {
    if(!Alive(r.pawn,r.index)||!Alive(r.controller,r.controllerIndex)||(U(r.pawn,0x2c)&0x10)||(U(r.controller,0x2c)&0x10)||
        U(r.pawn,0x6c)!=reinterpret_cast<DWORD>(sessionLevel)||static_cast<int>(U(r.pawn,healthOffset))<=0||
        U(r.pawn,controlOffset)!=reinterpret_cast<DWORD>(r.controller)||U(r.controller,pawnOffset)!=reinterpret_cast<DWORD>(r.pawn))return false;
    auto state=TrainerSpawn::State(r.controller,name),ps=TrainerSpawn::State(r.pawn,name);
    return (!wcscmp(state,L"s_Default")||!wcscmp(state,L"s_Investigate")||!wcscmp(state,L"s_Alert"))&&
        !TrainerSpawn::Unconscious(ps,state)&&wcscmp(ps,L"s_Grabbed");
}
static void Stop() {
    if(sessionTimer&&sessionWindow)KillTimer(sessionWindow,sessionTimer);
    sessionTimer=0;sessionWindow=nullptr;participantCount=0;sessionLevel=nullptr;
}
static void Maintain() {
    bool samePlayer=Alive(sessionPlayer,sessionPlayerIndex)&&Alive(sessionViewport,sessionViewportIndex)&&
        U(sessionViewport,0x34)==reinterpret_cast<DWORD>(sessionPlayer);
    bool sameLevel=samePlayer&&Alive(sessionLevel,levelIndex)&&U(sessionPlayer,0x6c)==reinterpret_cast<DWORD>(sessionLevel);
    if(!FightPolicy::MaintainSession(GetTickCount64(),deadline,samePlayer,sameLevel)){Stop();return;}
    FightPolicy::Candidate candidates[256]={};Record live[256]={};int count=0;
    for(int i=0;i<participantCount;++i)if(Live(participants[i])){
        const auto& r=participants[i];auto v=*reinterpret_cast<TrainerSpawn::Vec*>(r.pawn+0xd4);
        if(std::isfinite(v.x)&&std::isfinite(v.y)&&std::isfinite(v.z)){live[count]=r;candidates[count]={r.index,v.x,v.y,v.z};++count;}
    }
    for(int i=0;i<count;++i){
        auto& a=live[i];int j=FightPolicy::Nearest(candidates,count,i);
        auto list=reinterpret_cast<Byte*>(U(a.controller,goalListOffset));
        auto goal=IsA(list,L"EGoalList")?reinterpret_cast<Byte*>(U(list,headOffset)):nullptr;
        auto target=j<0?nullptr:live[j].pawn;
        // Ordinary perception and group scripts can set these back to Sam between ticks.
        *reinterpret_cast<DWORD*>(a.controller+seenOffset)&=~seenMask;
        *reinterpret_cast<Byte**>(a.controller+enemyOffset)=target;
        *reinterpret_cast<Byte**>(a.controller+fireTargetOffset)=target;
        if(j<0){
            // No opponent remains: hold position instead of giving native attack code a null target.
            *reinterpret_cast<DWORD*>(a.controller+shootOffset)&=~shootMask;
            if(IsA(goal,L"EGoal")){
                goal[goalTypeOffset]=static_cast<Byte>(guardType);
                memcpy(goal+goalLocationOffset,a.pawn+0xd4,12);memcpy(goal+goalFocusOffset,a.pawn+0xd4,12);
                *reinterpret_cast<DWORD*>(goal+initializedOffset)&=~initializedMask;
                *reinterpret_cast<Byte**>(goal+goalTargetOffset)=nullptr;
                *reinterpret_cast<DWORD*>(goal+goalUpdateOffset)&=~goalUpdateMask;
            }
            continue;
        }
        if(IsA(goal,L"EGoal")&&goal[goalTypeOffset]==attackType){
            *reinterpret_cast<Byte**>(goal+goalTargetOffset)=target;
            memcpy(goal+goalFocusOffset,target+0xd4,12);
            *reinterpret_cast<DWORD*>(goal+goalUpdateOffset)&=~goalUpdateMask;
        }else{
            alignas(4) Byte params[0x54]={};params[0]=static_cast<Byte>(attackType);params[1]=255;
            memcpy(params+4,a.pawn+0xd4,12);memcpy(params+0x10,target+0xd4,12);memcpy(params+0x1c,&target,4);
            DWORD force=1;float seconds=static_cast<float>(deadline>GetTickCount64()?deadline-GetTickCount64():0)/1000.0f;
            memcpy(params+0x2c,&force,4);memcpy(params+0x30,&seconds,4);
            params[0x34]=static_cast<Byte>(moveFlags);params[0x44]=static_cast<Byte>(moveFlags);
            dispatch(a.controller,a.add,params,nullptr);
        }
        if(wcscmp(TrainerSpawn::State(a.controller,name),L"s_Alert"))gotoState(a.controller,alertState);
    }
}
static void CALLBACK TimerProc(HWND,UINT,UINT_PTR,DWORD) {
    __try { Maintain(); } __except(EXCEPTION_EXECUTE_HANDLER) { Stop(); }
}
static bool Handle(Request* r,void* view,HMODULE core,DWORD* objects) {
    if(wcscmp(r->map,L"--guards-fight"))return false;
    objectArray=objects;name=reinterpret_cast<NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));
    auto find=reinterpret_cast<TrainerSpawn::FindFn>(GetProcAddress(core,"?FindFunction@UObject@@QAEPAVUFunction@@VFName@@H@Z"));
    auto process=reinterpret_cast<TrainerSpawn::EventFn>(GetProcAddress(core,"?ProcessEvent@UObject@@UAEXPAVUFunction@@PAX1@Z"));
    auto go=reinterpret_cast<int (__thiscall*)(void*,DWORD)>(GetProcAddress(core,"?GotoState@UObject@@UAE?AW4EGotoState@@VFName@@@Z"));
    if(!name||!find||!process||!go){r->result=49;return true;}
    auto pc=reinterpret_cast<Byte*>(U(static_cast<Byte*>(view),0x34));
    auto info=Alive(pc)?reinterpret_cast<Byte*>(U(pc,0x6c)):nullptr;
    auto package=Alive(info)?reinterpret_cast<Byte*>(U(info,0x18)):nullptr;
    if(!IsA(pc,L"EPlayerController")||!Alive(package)||!_wcsicmp(name(package),L"menu")){r->result=16;return true;}
    DWORD hp=Property(objects,name,L"Pawn",L"Health"),control=Property(objects,name,L"Pawn",L"Controller"),
        cp=Property(objects,name,L"Controller",L"Pawn"),weapon=Property(objects,name,L"EPawn",L"CurrentWeapon"),
        group=Property(objects,name,L"EAIController",L"Group");
    Field hostile={L"EPawn",L"bHostile",L"BoolProperty"};
    DWORD alert=0;auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i){
        auto obj=table[i];if(!Alive(obj))continue;
        auto owner=reinterpret_cast<Byte*>(U(obj,0x18));if(!Alive(owner))continue;
        if(!wcscmp(name(owner),L"EAIController")&&IsA(obj,L"State")&&!wcscmp(name(obj),L"s_Alert"))alert=U(obj,0x20);
        if(!wcscmp(name(owner),hostile.owner)&&!wcscmp(name(obj),hostile.name)&&IsA(obj,L"BoolProperty")){hostile.offset=U(obj,0x44);hostile.mask=U(obj,0x54);}
    }
    int attack=TrainerSpawn::EnumValue(core,objects,name,L"GoalType",L"GOAL_Attack");
    int guard=TrainerSpawn::EnumValue(core,objects,name,L"GoalType",L"GOAL_Guard");
    int flags=TrainerSpawn::EnumValue(core,objects,name,L"MoveFlags",L"MOVE_WalkAlert");
    if(!hp||!control||!cp||!weapon||!group||!hostile.offset||!hostile.mask||!alert||attack<0||guard<0||flags<0){r->result=49;return true;}
    Record records[256]={};FightPolicy::Candidate positions[256]={};int count=0;
    for(DWORD i=0;i<objects[1];++i){
        auto p=table[i];if(!IsA(p,L"EAIPawn")||IsA(p,L"EAINonHostile"))continue;
        auto ctrl=reinterpret_cast<Byte*>(U(p,control));
        auto w=reinterpret_cast<Byte*>(U(p,weapon));
        auto ps=TrainerSpawn::State(p,name),cs=IsA(ctrl,L"EAIController")?TrainerSpawn::State(ctrl,name):L"None";
        bool conscious=(!wcscmp(cs,L"s_Default")||!wcscmp(cs,L"s_Investigate")||!wcscmp(cs,L"s_Alert"))&&
            !TrainerSpawn::Unconscious(ps,cs)&&wcscmp(ps,L"s_Grabbed");
        bool controlled=IsA(ctrl,L"EAIController")&&U(ctrl,cp)==reinterpret_cast<DWORD>(p)&&
            !(U(ctrl,0x2c)&0x10)&&IsA(reinterpret_cast<Byte*>(U(ctrl,group)),L"EGroupAI");
        if(!FightPolicy::Eligible(true,(U(p,hostile.offset)&hostile.mask)!=0,U(p,0x6c)==reinterpret_cast<DWORD>(info),
            IsA(w,L"EWeapon"),conscious,controlled,(U(p,0x2c)&0x10)!=0,static_cast<int>(U(p,hp))))continue;
        if(count==256){r->result=49;return true;}
        auto location=*reinterpret_cast<TrainerSpawn::Vec*>(p+0xd4);
        if(!std::isfinite(location.x)||!std::isfinite(location.y)||!std::isfinite(location.z))continue;
        auto add=TrainerSpawn::Function(ctrl,L"AddGoal",objects,name,find);
        // Verify the complete input layout before changing any AI.
        if(!add||!TrainerSpawn::Param(add,L"_Type",0,1,objects,name)||!TrainerSpawn::Param(add,L"_Priority",1,1,objects,name)||
            !TrainerSpawn::Param(add,L"_Location",4,12,objects,name)||!TrainerSpawn::Param(add,L"_Focus",0x10,12,objects,name)||
            !TrainerSpawn::Param(add,L"_Target",0x1c,4,objects,name)||!TrainerSpawn::Param(add,L"_Flag",0x2c,4,objects,name)||
            !TrainerSpawn::Param(add,L"_Value",0x30,4,objects,name)||!TrainerSpawn::Param(add,L"_MoveFlags",0x34,1,objects,name)||
            !TrainerSpawn::Param(add,L"_WaitFlags",0x44,1,objects,name)||!TrainerSpawn::Param(add,L"_UpdatePlayerPos",0x48,4,objects,name)){
            r->result=49;return true;
        }
        records[count]={p,ctrl,U(p,4),U(ctrl,4),add};positions[count]={U(p,4),location.x,location.y,location.z};++count;
    }
    // Resolve every field required by the maintenance callback before issuing any orders.
    auto prop=[&](const wchar_t* owner,const wchar_t* field){return Property(objects,name,owner,field);};
    goalListOffset=prop(L"EAIController",L"m_pGoalList");headOffset=prop(L"EGoalList",L"pHead");
    goalTypeOffset=prop(L"EGoal",L"m_GoalType");goalTargetOffset=prop(L"EGoal",L"GoalTarget");
    goalFocusOffset=prop(L"EGoal",L"GoalFocus");fireTargetOffset=prop(L"EAIController",L"TargetActorToFire");
    enemyOffset=prop(L"Controller",L"Enemy");goalLocationOffset=prop(L"EGoal",L"GoalLocation");
    Field bools[]={ {L"EAIController",L"bPlayerSeen",L"BoolProperty"}, {L"EGoal",L"GoalUpdatePlayerPos",L"BoolProperty"}, {L"EAIController",L"bShootTarget",L"BoolProperty"}, {L"EGoal",L"bInitialized",L"BoolProperty"} };
    for(DWORD i=0;i<objects[1];++i){
        auto obj=reinterpret_cast<Byte**>(objects[0])[i];if(!IsA(obj,L"BoolProperty"))continue;
        auto owner=reinterpret_cast<Byte*>(U(obj,0x18));if(!Alive(owner))continue;
        for(auto& f:bools)if(!wcscmp(name(owner),f.owner)&&!wcscmp(name(obj),f.name)){f.offset=U(obj,0x44);f.mask=U(obj,0x54);}
    }
    if(!goalListOffset||!headOffset||!goalTypeOffset||!goalTargetOffset||!goalFocusOffset||!fireTargetOffset||!enemyOffset||
        !goalLocationOffset||!bools[0].offset||!bools[0].mask||!bools[1].offset||!bools[1].mask||!bools[2].offset||!bools[2].mask||!bools[3].offset||!bools[3].mask){r->result=49;return true;}
    Stop();
    healthOffset=hp;controlOffset=control;pawnOffset=cp;seenOffset=bools[0].offset;seenMask=bools[0].mask;
    shootOffset=bools[2].offset;shootMask=bools[2].mask;initializedOffset=bools[3].offset;initializedMask=bools[3].mask;guardType=guard;
    goalUpdateOffset=bools[1].offset;goalUpdateMask=bools[1].mask;attackType=attack;moveFlags=flags;alertState=alert;
    sessionViewport=static_cast<Byte*>(view);sessionViewportIndex=U(sessionViewport,4);sessionPlayer=pc;sessionPlayerIndex=U(pc,4);
    sessionLevel=info;levelIndex=U(info,4);sessionWindow=r->window;dispatch=process;gotoState=go;
    deadline=GetTickCount64()+30000;participantCount=count;memcpy(participants,records,sizeof(Record)*count);
    sessionTimer=SetTimer(sessionWindow,0x53434654,16,&TimerProc);
    if(!sessionTimer){Stop();r->result=49;return true;}
    int changed=0;
    for(int i=0;i<count;++i){
        int j=FightPolicy::Nearest(positions,count,i);if(j<0)continue;
        auto& a=records[i];auto& b=records[j];
        if(!Alive(a.pawn,a.index)||!Alive(a.controller,a.controllerIndex)||!Alive(b.pawn,b.index))continue;
        if(static_cast<int>(U(a.pawn,hp))<=0||static_cast<int>(U(b.pawn,hp))<=0)continue;
        alignas(4) Byte params[0x54]={};params[0]=static_cast<Byte>(attack);params[1]=255;
        // Hold the current position and aim at another guard; never update Sam's location.
        memcpy(params+4,a.pawn+0xd4,12);memcpy(params+0x10,b.pawn+0xd4,12);memcpy(params+0x1c,&b.pawn,4);
        DWORD force=1;float seconds=30;memcpy(params+0x2c,&force,4);memcpy(params+0x30,&seconds,4);
        params[0x34]=static_cast<Byte>(flags);params[0x44]=static_cast<Byte>(flags);
        go(a.controller,alert);process(a.controller,a.add,params,nullptr);++changed;
    }
    Maintain();
    swprintf_s(r->map,L"Maintaining %d guards against nearby guards for 30 seconds.",changed);
    r->result=48;return true;
}
}
