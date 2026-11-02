#pragma once
#include "FreezePolicy.h"
// Uses the same per-pawn flag as EPlayerController.FreezePawns, with reversible ownership.
namespace TrainerFreeze {
using namespace TrainerCheats;
struct Entry {Byte* pawn;DWORD index,name,original;};
static std::vector<Entry> frozen;
static Byte* activeViewport=nullptr;static DWORD viewportIndex=0;
static DWORD offset=0,mask=0;static HWND ownerWindow=nullptr;static UINT_PTR timerId=0;static ULONGLONG heartbeat=0;
static bool Resolve(){
    if(offset)return true;auto table=reinterpret_cast<Byte**>(objectArray[0]);
    for(DWORD i=0;i<objectArray[1];++i){auto obj=table[i];if(!IsA(obj,L"BoolProperty")||wcscmp(name(obj),L"bDisableAI"))continue;
        auto owner=reinterpret_cast<Byte*>(U(obj,0x18));if(!Alive(owner)||wcscmp(name(owner),L"EPawn"))continue;
        DWORD o=U(obj,0x44),m=U(obj,0x54);if(o<0x28||o>0x8000||!m||(m&(m-1)))return false;offset=o;mask=m;return true;
    }return false;
}
static bool Valid(const Entry& e){return Alive(e.pawn,e.index)&&U(e.pawn,0x20)==e.name&&!(U(e.pawn,0x2c)&0x10);}
static void Restore(){
    for(const auto& e:frozen)if(Valid(e))*reinterpret_cast<DWORD*>(e.pawn+offset)=FreezePolicy::RestoreBits(U(e.pawn,offset),e.original,mask);
    frozen.clear();if(timerId&&ownerWindow)KillTimer(ownerWindow,timerId);timerId=0;ownerWindow=nullptr;
}
static void CALLBACK Timer(HWND,UINT,UINT_PTR,DWORD){
    __try{
        auto pc=Alive(activeViewport,viewportIndex)?reinterpret_cast<Byte*>(U(activeViewport,0x34)):nullptr;
        if(FreezePolicy::Expired(GetTickCount64(),heartbeat)||!Alive(pc)||(!frozen.empty()&&Valid(frozen.front())&&U(pc,0x6c)!=U(frozen.front().pawn,0x6c))){Restore();return;}
        for(auto i=frozen.begin();i!=frozen.end();)if(!Valid(*i))i=frozen.erase(i);else{*reinterpret_cast<DWORD*>(i->pawn+offset)|=mask;++i;}
        if(frozen.empty())Restore();
    }__except(EXCEPTION_EXECUTE_HANDLER){if(timerId&&ownerWindow)KillTimer(ownerWindow,timerId);timerId=0;}
}
static bool Set(Byte* npc,bool enable,HWND targetWindow,Byte* view){
    if(!Resolve()||!IsA(npc,L"EAIPawn"))return false;
    auto found=std::find_if(frozen.begin(),frozen.end(),[&](const Entry& e){return e.pawn==npc&&e.index==U(npc,4)&&e.name==U(npc,0x20);});
    if(enable){
        if(found==frozen.end())frozen.push_back({npc,U(npc,4),U(npc,0x20),U(npc,offset)&mask});
        activeViewport=view;viewportIndex=U(view,4);ownerWindow=targetWindow;heartbeat=GetTickCount64();
        if(!timerId)timerId=SetTimer(targetWindow,0x53434652,100,&Timer);
        if(!timerId){Restore();return false;}
        *reinterpret_cast<DWORD*>(npc+offset)|=mask;
    }else if(found!=frozen.end()){
        if(Valid(*found))*reinterpret_cast<DWORD*>(npc+offset)=(U(npc,offset)&~mask)|found->original;
        frozen.erase(found);if(frozen.empty())Restore();
    }
    return true;
}
static bool Handle(Request* r,HMODULE core,DWORD* objects){
    bool ping=!wcscmp(r->map,L"--freeze-ping"),off=!wcscmp(r->map,L"--freeze-off");if(!ping&&!off)return false;
    objectArray=objects;name=reinterpret_cast<NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));if(!name){r->result=57;return true;}
    if(off)Restore();else heartbeat=GetTickCount64();
    swprintf_s(r->map,L"FROZEN:%u",static_cast<unsigned>(frozen.size()));r->result=58;return true;
}
}
