#pragma once
#include <vector>
#include <cmath>
#include <algorithm>

// All calls and timer callbacks execute on the game's window thread.
namespace TrainerCheats {
using Byte = unsigned char;
using NameFn = const wchar_t* (__thiscall*)(void*);
struct Field { const wchar_t* owner; const wchar_t* name; const wchar_t* type; DWORD offset=0, mask=0; WORD count=0; };
enum { Ammo=1, NoReload=2, Gadgets=4, Detection=8, Speed=16, Invisible=32, Noclip=64, BypassLocks=128, QuietLights=256, CamerasOff=512, TurretsOff=1024, IffOff=2048, SilentMovement=4096, SilentSam=8192 };
static Field fields[] = {
    {L"EPlayerController",L"EPawn",L"ObjectProperty"},
    {L"EPawn",L"FullInventory",L"ObjectProperty"},
    {L"EInventory",L"InventoryList",L"ArrayProperty"},
    {L"EInventoryItem",L"Quantity",L"IntProperty"},
    {L"EInventoryItem",L"MaxQuantity",L"IntProperty"},
    {L"EWeapon",L"Ammo",L"IntProperty"},
    {L"EWeapon",L"MaxAmmo",L"IntProperty"},
    {L"EWeapon",L"ClipAmmo",L"IntProperty"},
    {L"EWeapon",L"ClipMaxAmmo",L"IntProperty"},
    {L"EPlayerController",L"bFullAmmo",L"BoolProperty"},
    {L"EPawn",L"bHostile",L"BoolProperty"},
    {L"EPlayerController",L"m_speedRun",L"FloatProperty"},
    {L"EPlayerController",L"m_speedRunCr",L"FloatProperty"},
    {L"EPlayerController",L"m_speedWalk",L"FloatProperty"},
    {L"EPlayerController",L"m_speedWalkCr",L"FloatProperty"},
    {L"EPlayerController",L"m_speedWalkFP",L"FloatProperty"},
    {L"EPlayerController",L"m_speedWalkFPCr",L"FloatProperty"},
    {L"EPlayerController",L"m_speedWalkSniping",L"FloatProperty"},
    {L"EPlayerController",L"m_speedBTWCr",L"FloatProperty"},
    {L"EPlayerController",L"m_speedBTW",L"FloatProperty"},
    {L"EPawn",L"m_VisibilityMaxDistance",L"FloatProperty"},
    {L"EPawn",L"m_MaxPeripheralVisionDistance",L"FloatProperty"},
    {L"EPawn",L"VisTable_Alert",L"FloatProperty"},
    {L"EPawn",L"VisTable_Investigate",L"FloatProperty"},
    {L"EPawn",L"VisTable_Surprised",L"FloatProperty"},
    {L"EPawn",L"VisTable_Peripheral",L"FloatProperty"},
    {L"Actor",L"bHidden",L"BoolProperty"},
    {L"Pawn",L"Visibility",L"ByteProperty"},
    {L"Actor",L"bCollideWorld",L"BoolProperty"}
};
struct Saved { Byte* object; DWORD index,offset,mask,original,last,group; };
struct Array { Byte* data; int count,capacity; };
static DWORD* objectArray=nullptr;
static NameFn name=nullptr;
static Byte *viewport=nullptr,*player=nullptr,*pawn=nullptr;
static DWORD playerIndex=0;
static volatile LONG activeMask=0;
static int speedPercent=100;
static HWND window=nullptr;
static UINT_PTR timer=0;
static ULONGLONG heartbeat=0,nextGuards=0;
static std::vector<Saved> saved;
static std::vector<Byte*> guards;
static DWORD U(Byte* p,DWORD o=0){return *reinterpret_cast<DWORD*>(p+o);}
static bool Alive(Byte* p,DWORD index) {
    return p && objectArray && index<objectArray[1] && index<1000000 &&
        reinterpret_cast<Byte**>(objectArray[0])[index]==p;
}
static bool Alive(Byte* p){return p && Alive(p,U(p,4));}
static bool IsA(Byte* p,const wchar_t* wanted) {
    if(!Alive(p))return false;
    auto cls=reinterpret_cast<Byte*>(U(p,0x24));
    for(int i=0;cls && i<24;++i) {
        if(!wcscmp(name(cls),wanted))return true;
        cls=reinterpret_cast<Byte*>(U(cls,0x2c));
    }
    return false;
}
static bool Current() {
    return window && IsWindow(window) && Alive(player,playerIndex) && Alive(viewport) &&
        reinterpret_cast<Byte*>(U(viewport,0x34))==player && Alive(pawn) &&
        reinterpret_cast<Byte*>(U(player,fields[0].offset))==pawn;
}
static void Resolve() {
    for(auto& f:fields){f.offset=0;f.mask=0;f.count=0;}
    auto table=reinterpret_cast<Byte**>(objectArray[0]); DWORD count=objectArray[1];
    if(count>1000000)throw 1;
    for(DWORD i=0;i<count;++i) {
        auto obj=table[i];if(!obj)continue;
        const auto n=name(obj);
        for(auto& f:fields) {
            if(wcscmp(n,f.name))continue;
            auto owner=reinterpret_cast<Byte*>(U(obj,0x18)),type=reinterpret_cast<Byte*>(U(obj,0x24));
            if(!owner||!type||wcscmp(name(owner),f.owner)||wcscmp(name(type),f.type))continue;
            f.offset=U(obj,0x44);f.mask=U(obj,0x54);f.count=*reinterpret_cast<WORD*>(obj+0x38);
        }
    }
    for(const auto& f:fields) {
        if(f.offset<0x28||f.offset>0x8000||!f.count||f.count>64)throw 1;
        if(!wcscmp(f.type,L"BoolProperty") && (!f.mask || (f.mask&(f.mask-1))))throw 1;
    }
}
static void NoiseRestore();
static void NoiseResolve();
static void SecurityRestore(DWORD groups);
static void SecurityClear();
static void SecurityResolve();
static void SecurityTick();
static void QuietRestore();
static void QuietClear();
static void QuietResolve();
static void QuietTick();
static void LockRestore();
static void LockClear();
static void LockResolve();
static void LockTick();
static bool ghostOwned=false;
static bool Command(const wchar_t* command) {
    auto engine=GetModuleHandleW(L"Core.dll");
    auto log=reinterpret_cast<void**>(GetProcAddress(engine,"?GLog@@3PAVFOutputDevice@@A"));
    auto target=viewport+0x30;
    auto exec=reinterpret_cast<int (__thiscall*)(void*,const wchar_t*,void*)>((*reinterpret_cast<void***>(target))[0]);
    return log&&*log&&exec(target,command,*log)!=0;
}
static void Restore(DWORD groups) {
    if(groups&(CamerasOff|TurretsOff|IffOff))SecurityRestore(groups);
    if(groups&BypassLocks)LockRestore();
    if(groups&QuietLights)QuietRestore();
    if((groups&Noclip)&&ghostOwned) {
        if(Current()&&!Command(L"Walk"))throw 1;
        ghostOwned=false;
    }
    for(auto it=saved.begin();it!=saved.end();) {
        if(!(it->group&groups)){++it;continue;}
        if(Alive(it->object,it->index)) {
            auto address=reinterpret_cast<DWORD*>(it->object+it->offset);
            if(it->mask) {
                InterlockedAnd(reinterpret_cast<volatile LONG*>(address),~it->mask);
                InterlockedOr(reinterpret_cast<volatile LONG*>(address),it->original & it->mask);
            } else if(*address==it->last) *address=it->original;
        }
        it=saved.erase(it);
    }
}
static void Stop(bool restore) {
    NoiseRestore();
    if(restore)Restore(16383);
    else {LockClear();QuietClear();SecurityClear();}
    saved.clear();guards.clear();activeMask=0;
    if(timer && window)KillTimer(window,timer);
    timer=0;
}
static void Scale(Byte* object,DWORD offset,float factor,DWORD group) {
    auto index=U(object,4);
    Saved* slot=nullptr;
    for(auto& item:saved)if(item.object==object&&item.index==index&&item.offset==offset&&item.group==group){slot=&item;break;}
    if(!slot){saved.push_back({object,index,offset,0,U(object,offset),0,group});slot=&saved.back();}
    float original;memcpy(&original,&slot->original,4);
    if(!std::isfinite(original)||original<0||original>1000000)throw 1;
    float value=original*factor;
    memcpy(&slot->last,&value,4);
    *reinterpret_cast<float*>(object+offset)=value;
}
static void FullAmmoFlag() {
    const auto& f=fields[9]; Saved* slot=nullptr;
    for(auto& item:saved)if(item.object==player&&item.group==NoReload){slot=&item;break;}
    if(!slot){saved.push_back({player,playerIndex,f.offset,f.mask,U(player,f.offset),0,NoReload});}
    InterlockedOr(reinterpret_cast<volatile LONG*>(player+f.offset),f.mask);
}
static void InvisiblePlayer() {
    const auto& hidden=fields[26];const auto& visibility=fields[27];
    for(int i=0;i<2;++i) {
        DWORD offset=i?visibility.offset:hidden.offset,mask=i?255:hidden.mask;
        Saved* slot=nullptr;
        for(auto& item:saved)if(item.object==pawn&&item.index==U(pawn,4)&&item.offset==offset&&item.group==Invisible){slot=&item;break;}
        if(!slot)saved.push_back({pawn,U(pawn,4),offset,mask,U(pawn,offset),0,Invisible});
        if(i)*reinterpret_cast<Byte*>(pawn+offset)=0;
        else InterlockedOr(reinterpret_cast<volatile LONG*>(pawn+offset),mask);
    }
}
static bool Consumable(Byte* item) {
    static const wchar_t* types[]={L"ESecondaryAmmo",L"EMedKit",L"EFragGrenade",L"EChemFlare",L"EFlare",L"EWallMine",L"EDisposablePick",L"ESmokeGrenade",L"EStickyCamera",L"EDiversionCamera",L"EStickyShocker",L"ERingAirfoilRound"};
    for(auto t:types)if(IsA(item,t))return true;
    return false;
}
static void Inventory() {
    auto inv=reinterpret_cast<Byte*>(U(pawn,fields[1].offset));
    if(!IsA(inv,L"EInventory"))return;
    auto cats=*reinterpret_cast<Array*>(inv+fields[2].offset);
    if(cats.count<0||cats.count>16||cats.count>cats.capacity||!cats.data)return;
    for(int c=0;c<cats.count;++c) {
        auto items=reinterpret_cast<Array*>(cats.data)[c];
        if(items.count<0||items.count>128||items.count>items.capacity||!items.data)continue;
        for(int j=0;j<items.count;++j) {
            auto item=reinterpret_cast<Byte**>(items.data)[j];
            if(!IsA(item,L"EInventoryItem"))continue;
            if(IsA(item,L"EWeapon")) {
                int max=*reinterpret_cast<int*>(item+fields[6].offset);
                int clipMax=*reinterpret_cast<int*>(item+fields[8].offset);
                if(max<1||max>100000||clipMax<1||clipMax>1000)continue;
                if(activeMask&Ammo)*reinterpret_cast<int*>(item+fields[5].offset)=max;
                if(activeMask&NoReload) {
                    *reinterpret_cast<int*>(item+fields[7].offset)=clipMax;
                    auto total=reinterpret_cast<int*>(item+fields[5].offset);
                    if(*total<clipMax)*total=clipMax;
                }
            } else if((activeMask&Gadgets)&&Consumable(item)) {
                int max=*reinterpret_cast<int*>(item+fields[4].offset);
                if(max>0&&max<10000)*reinterpret_cast<int*>(item+fields[3].offset)=(max > 2 ? max : 2);
            }
        }
    }
}
static void FindGuards() {
    guards.clear();
    auto table=reinterpret_cast<Byte**>(objectArray[0]);auto count=objectArray[1];
    if(count>1000000)throw 1;
    for(DWORD i=0;i<count;++i) {
        auto p=table[i];
        if(!IsA(p,L"EAIPawn")||IsA(p,L"EAINonHostile"))continue;
        if(U(p,0x6c)!=U(player,0x6c)||!(U(p,fields[10].offset)&fields[10].mask))continue;
        guards.push_back(p);
    }
    nextGuards=GetTickCount64()+1500;
}
#include "Locks.h"
#include "QuietLights.h"
#include "Security.h"
#include "SilentNoise.h"

static void Tick() {
    if(!Current()||GetTickCount64()-heartbeat>15000){Stop(true);return;}
    if(activeMask&BypassLocks)LockTick();
    if(activeMask&QuietLights)QuietTick();
    if(activeMask&(CamerasOff|TurretsOff|IffOff))SecurityTick();
    if(activeMask&Invisible)InvisiblePlayer();
    if(activeMask&NoReload)FullAmmoFlag();
    if(activeMask&(Ammo|NoReload|Gadgets))Inventory();
    if(activeMask&Speed)for(int i=11;i<=19;++i)Scale(player,fields[i].offset,speedPercent/100.0f,Speed);
    if(activeMask&Detection) {
        if(GetTickCount64()>=nextGuards)FindGuards();
        for(auto p:guards) {
            if(!Alive(p))continue;
            for(int f=20;f<=25;++f)for(int i=0;i<fields[f].count;++i)
                Scale(p,fields[f].offset+i*4,0.25f,Detection);
        }
    }
}
static bool SafeTick() {
    __try{Tick();return true;}
    __except(EXCEPTION_EXECUTE_HANDLER){Stop(false);return false;}
}
static void CALLBACK TimerProc(HWND,UINT,UINT_PTR,DWORD) {
    try{SafeTick();}catch(...){Stop(false);}
}
static bool HandleRequest(Request* r,void* view,HMODULE core,DWORD* objects) {
    bool ping=!wcscmp(r->map,L"--cheats-ping");
    if(!ping && wcsncmp(r->map,L"--cheats:",9))return false;
    objectArray=objects;
    name=reinterpret_cast<NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));
    if(!name){r->result=3;return true;}
    if(ping) {
        if(!Current()){Stop(true);r->result=17;return true;}
        heartbeat=GetTickCount64();
    } else {
        unsigned mask=0;int percent=0;wchar_t extra=0;
        if(swscanf_s(r->map,L"--cheats:%u:%d%c",&mask,&percent,&extra,1)!=2||mask>16383||percent<50||percent>300){r->result=6;return true;}
        auto vp=static_cast<Byte*>(view);
        auto pc=reinterpret_cast<Byte*>(U(vp,0x34));
        if(activeMask && (pc!=player||!Current()))Stop(true);
        if(mask==0)Stop(true);
        else {
            if(!activeMask) {
                Resolve();
                auto level=pc?reinterpret_cast<Byte*>(U(pc,0x6c)):nullptr;
                auto package=level?reinterpret_cast<void*>(U(level,0x18)):nullptr;
                if(!IsA(pc,L"EPlayerController")||!package||!_wcsicmp(name(package),L"menu")){r->result=16;return true;}
                viewport=vp;player=pc;playerIndex=U(pc,4);pawn=reinterpret_cast<Byte*>(U(pc,fields[0].offset));window=r->window;
                if(!IsA(pawn,L"EPawn")){r->result=16;return true;}
            }
            if((mask&Noclip)&&!(activeMask&Noclip)) {
                auto frame=reinterpret_cast<Byte*>(U(player,0x0c));
                auto state=frame?reinterpret_cast<Byte*>(U(frame,0x1c)):nullptr;
                // Avoid interrupting ladders, interactions, death or scripted movement.
                if(!Alive(state)||wcscmp(name(state),L"s_PlayerWalking")){r->result=43;return true;}
                ghostOwned=true;
                if(!Command(L"Ghost")||(U(pawn,fields[28].offset)&fields[28].mask)) {
                    Restore(Noclip);r->result=3;return true;
                }
            }
            if((mask&BypassLocks)&&!(activeMask&BypassLocks)){try{LockResolve();}catch(...){r->result=44;return true;}}
            if((mask&QuietLights)&&!(activeMask&QuietLights)){try{QuietResolve();}catch(...){r->result=45;return true;}}
            if((mask&(CamerasOff|TurretsOff|IffOff))&&!(activeMask&(CamerasOff|TurretsOff|IffOff))){try{SecurityResolve();}catch(...){r->result=67;return true;}}
            if((mask&(SilentMovement|SilentSam))&&!noiseInstalled){try{NoiseResolve();}catch(...){r->result=74;return true;}}
            Restore(static_cast<DWORD>(activeMask)&~mask);
            if(!(mask&(SilentMovement|SilentSam)))NoiseRestore();
            activeMask=mask;speedPercent=percent;heartbeat=GetTickCount64();nextGuards=0;
            Tick();
            if(!timer)timer=SetTimer(window,0x53435452,16,&TimerProc);
            if(!timer){Stop(true);r->result=7;return true;}
        }
    }
    swprintf_s(r->map,L"%08lX:%08lX:%08lX:%08lX",
        reinterpret_cast<DWORD>(viewport),reinterpret_cast<DWORD>(player),playerIndex,reinterpret_cast<DWORD>(&activeMask));
    r->result=18;return true;
}
static bool SafeRequest(Request* r,void* view,HMODULE core,DWORD* objects) {
    try{return HandleRequest(r,view,core,objects);}
    catch(...){Stop(true);r->result=3;return true;}
}
} // namespace TrainerCheats
