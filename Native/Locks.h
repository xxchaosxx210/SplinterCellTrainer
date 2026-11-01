#pragma once
#include "LockPolicy.h"
// Included inside TrainerCheats; every operation executes on the game window thread.
static Field lockFields[]={
    {L"EDoorMover",L"Locked",L"BoolProperty"},
    {L"EDoorMover",L"Usable",L"BoolProperty"},
    {L"ESwingingDoor",L"MyKnob",L"ObjectProperty"},
    {L"EDoorMover",L"FrontOpener",L"ObjectProperty"},
    {L"EDoorMover",L"BackOpener",L"ObjectProperty"},
    {L"EDoorMover",L"LinkedDoor",L"ObjectProperty"},
    {L"Mover",L"bClosed",L"BoolProperty"},
    {L"Actor",L"Interaction",L"ObjectProperty"},
    {L"EDoorInteraction",L"MyDoor",L"ObjectProperty"},
    {L"EKeyPadInteraction",L"MyKeyPad",L"ObjectProperty"},
    {L"EPopObjectInteraction",L"InteractionController",L"ObjectProperty"},
    {L"Actor",L"Location",L"StructProperty"}
};
struct LockDoorRef {Byte* object;DWORD index,objectName;};
static std::vector<LockDoorRef> lockDoors,preparedDoors;
static bool locksReady=false;
static ULONGLONG nextDoorScan=0,nextLockTick=0;
static DWORD grantedState=0;
static Byte* refreshDoorFunction=nullptr;
using LockGotoFn=int (__thiscall*)(void*,DWORD);
using LockEventFn=void (__thiscall*)(void*,void*,void*,void*);
using LockFindFn=void* (__thiscall*)(void*,DWORD,int);
static LockGotoFn lockGoto=nullptr;
static LockEventFn lockEvent=nullptr;
static LockFindFn lockFind=nullptr;
static const wchar_t* LockState(Byte* object) {
    if(!Alive(object))return L"None";
    auto frame=reinterpret_cast<Byte*>(U(object,0x0c));
    auto state=frame?reinterpret_cast<Byte*>(U(frame,0x1c)):nullptr;
    return Alive(state)?name(state):L"None";
}
static bool LockRefAlive(const LockDoorRef& ref) {
    return Alive(ref.object,ref.index)&&U(ref.object,0x20)==ref.objectName&&!(U(ref.object,0x2c)&0x10);
}
static bool LockSameLevel(Byte* object) {return Alive(object)&&U(object,0x6c)==U(player,0x6c);}
static bool LockFlag(Byte* object,int field){const auto& f=lockFields[field];return (U(object,f.offset)&f.mask)!=0;}
static Byte* LockObject(Byte* object,int field){return reinterpret_cast<Byte*>(U(object,lockFields[field].offset));}
static bool LockPlainDoor(Byte* door) {
    if(!IsA(door,L"ESwingingDoor")||!LockSameLevel(door))return false;
    auto knob=LockObject(door,2);
    return IsA(knob,L"EKnob")&&LockObject(door,3)==knob&&LockObject(door,4)==knob;
}
static float LockDistanceSquared(Byte* door) {
    auto a=reinterpret_cast<float*>(door+lockFields[11].offset);
    auto b=reinterpret_cast<float*>(pawn+lockFields[11].offset);
    float x=a[0]-b[0],y=a[1]-b[1],z=a[2]-b[2];return x*x+y*y+z*z;
}
static bool LockCommitted(Byte* door) {
    if(!Current()||wcscmp(LockState(player),L"s_OpenDoor"))return false;
    auto interaction=LockObject(player,7);
    return IsA(interaction,L"EDoorInteraction")&&LockObject(interaction,8)==door;
}
static void LockRefreshDoor(Byte* door) {
    if(!Current()||!LockSameLevel(door)||!Alive(refreshDoorFunction)||!lockEvent||!lockFind)return;
    auto knob=LockObject(door,2);
    if(!IsA(knob,L"EKnob"))return;
    auto interaction=LockObject(knob,7);
    if(!IsA(interaction,L"EDoorInteraction"))return;
    auto function=lockFind(interaction,U(refreshDoorFunction,0x20),0);
    if(function)lockEvent(interaction,function,nullptr,nullptr);
}
static void LockRestoreOne(const LockDoorRef& ref) {
    if(!LockRefAlive(ref)||!LockPolicy::RestoreDoor(LockFlag(ref.object,6),LockCommitted(ref.object)))return;
    const auto& f=lockFields[0];
    // Only restore a lock still in the value written by this feature.
    if(!LockFlag(ref.object,0))InterlockedOr(reinterpret_cast<volatile LONG*>(ref.object+f.offset),f.mask);
    LockRefreshDoor(ref.object);
}
static void LockRestore() {
    if(locksReady)for(const auto& ref:preparedDoors)LockRestoreOne(ref);
    preparedDoors.clear();lockDoors.clear();nextDoorScan=0;nextLockTick=0;
}
static void LockClear(){preparedDoors.clear();lockDoors.clear();nextDoorScan=0;nextLockTick=0;}
static void LockResolve() {
    locksReady=false;
    for(auto& f:lockFields){f.offset=0;f.mask=0;f.count=0;}
    refreshDoorFunction=nullptr;grantedState=0;
    auto core=GetModuleHandleW(L"Core.dll");
    lockGoto=reinterpret_cast<LockGotoFn>(GetProcAddress(core,"?GotoState@UObject@@UAE?AW4EGotoState@@VFName@@@Z"));
    lockEvent=reinterpret_cast<LockEventFn>(GetProcAddress(core,"?ProcessEvent@UObject@@UAEXPAVUFunction@@PAX1@Z"));
    lockFind=reinterpret_cast<LockFindFn>(GetProcAddress(core,"?FindFunction@UObject@@QAEPAVUFunction@@VFName@@H@Z"));
    if(!lockGoto||!lockEvent||!lockFind)throw 1;
    auto table=reinterpret_cast<Byte**>(objectArray[0]);auto count=objectArray[1];if(count>1000000)throw 1;
    for(DWORD i=0;i<count;++i){
        auto object=table[i];if(!object)continue;
        auto owner=reinterpret_cast<Byte*>(U(object,0x18));auto type=reinterpret_cast<Byte*>(U(object,0x24));
        if(!owner||!type)continue;
        const auto n=name(object),on=name(owner),tn=name(type);
        if(!wcscmp(on,L"EKeyPad")&&!wcscmp(tn,L"State")&&!wcscmp(n,L"s_AccessGranted"))grantedState=U(object,0x20);
        if(!wcscmp(on,L"EDoorInteraction")&&!wcscmp(tn,L"Function")&&!wcscmp(n,L"RefreshInteractions"))refreshDoorFunction=object;
        for(auto& f:lockFields)if(!wcscmp(n,f.name)&&!wcscmp(on,f.owner)&&!wcscmp(tn,f.type)){
            f.offset=U(object,0x44);f.mask=U(object,0x54);f.count=*reinterpret_cast<WORD*>(object+0x38);
        }
    }
    for(const auto& f:lockFields){
        if(f.offset<0x28||f.offset>0x8000||f.count!=1)throw 1;
        if(!wcscmp(f.type,L"BoolProperty")&&(!f.mask||(f.mask&(f.mask-1))))throw 1;
    }
    if(!grantedState)throw 1;
    locksReady=true;nextDoorScan=0;nextLockTick=0;
}
static void LockPrepare(Byte* door,float radiusSquared) {
    if(!LockPlainDoor(door)||!LockPolicy::PrepareDoor(LockFlag(door,1),LockFlag(door,0),LockFlag(door,6),false,LockDistanceSquared(door),radiusSquared))return;
    for(const auto& ref:preparedDoors)if(ref.object==door&&ref.index==U(door,4))return;
    if(preparedDoors.size()>=64)return;
    preparedDoors.push_back({door,U(door,4),U(door,0x20)});
    InterlockedAnd(reinterpret_cast<volatile LONG*>(door+lockFields[0].offset),~lockFields[0].mask);
    LockRefreshDoor(door);
}
static void LockTick() {
    if(!locksReady)return;
    auto now=GetTickCount64();if(now<nextLockTick)return;nextLockTick=now+50;
    if(now>=nextDoorScan){
        lockDoors.clear();auto table=reinterpret_cast<Byte**>(objectArray[0]);auto count=objectArray[1];if(count>1000000)throw 1;
        for(DWORD i=0;i<count;++i){auto door=table[i];if(IsA(door,L"ESwingingDoor")&&LockSameLevel(door)&&!(U(door,0x2c)&0x10))lockDoors.push_back({door,U(door,4),U(door,0x20)});}
        nextDoorScan=now+1000;
    }
    for(auto it=preparedDoors.begin();it!=preparedDoors.end();){
        if(!LockRefAlive(*it)){it=preparedDoors.erase(it);continue;}
        auto door=it->object;
        // Normal opening commits the unlock; do not relock normal mission progress.
        if(!LockPolicy::RestoreDoor(LockFlag(door,6),LockCommitted(door))){it=preparedDoors.erase(it);continue;}
        if(!LockPlainDoor(door)||!LockFlag(door,1)||LockDistanceSquared(door)>LockPolicy::RadiusSquared*4){LockRestoreOne(*it);it=preparedDoors.erase(it);continue;}
        ++it;
    }
    if(wcscmp(LockState(player),L"s_PickLock"))for(const auto& ref:lockDoors){
        if(!LockRefAlive(ref))continue;
        auto door=ref.object;
        if(LockPlainDoor(door)&&LockPolicy::PrepareDoor(LockFlag(door,1),LockFlag(door,0),LockFlag(door,6),false,LockDistanceSquared(door))){
            LockPrepare(door,LockPolicy::RadiusSquared);
            auto linked=LockObject(door,5);if(linked)LockPrepare(linked,LockPolicy::RadiusSquared*4);
        }
    }
    auto interaction=LockObject(player,7);
    if(!IsA(interaction,L"EKeyPadInteraction"))return;
    auto keypad=LockObject(interaction,9);
    if(!IsA(keypad,L"EKeyPad")||!LockSameLevel(keypad))return;
    if(LockPolicy::GrantKeypad(!wcscmp(LockState(player),L"s_KeyPadInteract"),LockObject(interaction,10)==player,!wcscmp(LockState(keypad),L"s_Use")))
        lockGoto(keypad,grantedState); // Normal access-granted code owns sounds, linked actors and mission events.
}
