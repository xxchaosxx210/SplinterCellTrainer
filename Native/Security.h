#pragma once
#include "SecurityPolicy.h"
// Included within TrainerCheats. Only same-level cameras/turrets are affected.
static Field securityFields[]={
 {L"ESensor",L"SoundDisable",L"ObjectProperty"},
 {L"EGameplayObject",L"ChangeListWhenDamaged",L"BoolProperty"},
 {L"ESensor",L"SensorDetectionType",L"ByteProperty"},
 {L"ESensor",L"Target",L"ObjectProperty"}
};
struct SecurityRef {Byte* actor;DWORD index,objectName,level,group,original;};
static std::vector<SecurityRef> securityStates,securityIff;
static DWORD securityDisabled=0,securityPatrol=0;static int securityAllPawns=-1;
static int (__thiscall* securityGo)(void*,DWORD)=nullptr;
static ULONGLONG nextSecurity=0;
static Byte* SecurityState(Byte* actor){auto frame=reinterpret_cast<Byte*>(U(actor,0x0c));auto state=frame?reinterpret_cast<Byte*>(U(frame,0x1c)):nullptr;return Alive(state)?state:nullptr;}
static bool SecurityDestroyed(Byte* actor){auto state=SecurityState(actor);return state&&(!_wcsicmp(name(state),L"s_Destructed")||!_wcsicmp(name(state),L"s_Jammed"));}
static bool SecurityValid(const SecurityRef& r){return Alive(r.actor,r.index)&&U(r.actor,0x20)==r.objectName&&U(r.actor,0x6c)==r.level&&
 Alive(player,playerIndex)&&U(player,0x6c)==r.level&&!(U(r.actor,0x2c)&0x10);}
static void SecurityQuietGo(Byte* actor,DWORD state){
 auto sound=reinterpret_cast<DWORD*>(actor+securityFields[0].offset),change=reinterpret_cast<DWORD*>(actor+securityFields[1].offset);
 DWORD oldSound=*sound,oldChange=*change&securityFields[1].mask;
 __try{*sound=0;*change&=~securityFields[1].mask;securityGo(actor,state);}
 __finally{*sound=oldSound;*change=(*change&~securityFields[1].mask)|oldChange;}
}
static void SecurityClear(){securityStates.clear();securityIff.clear();nextSecurity=0;}
static void SecurityRestore(DWORD groups){
 for(auto i=securityIff.begin();i!=securityIff.end();)if(!(i->group&groups))++i;else{
  if(SecurityPolicy::RestoreIff(SecurityValid(*i),SecurityValid(*i)?*(i->actor+securityFields[2].offset):0,static_cast<unsigned>(securityAllPawns))){
   bool changed=*(i->actor+securityFields[2].offset)!=i->original;
   *(i->actor+securityFields[2].offset)=static_cast<Byte>(i->original);
   auto state=SecurityState(i->actor);
   if(changed&&state&&!_wcsicmp(name(state),L"s_Alert"))SecurityQuietGo(i->actor,securityPatrol);
  }
  i=securityIff.erase(i);
 }
 for(auto i=securityStates.begin();i!=securityStates.end();)if(!(i->group&groups))++i;else{
  bool valid=SecurityValid(*i);auto current=valid?SecurityState(i->actor):nullptr;
  if(SecurityPolicy::RestoreState(valid,current&&U(current,0x20)==securityDisabled,valid&&SecurityDestroyed(i->actor))){
   DWORD state=i->original;
   // An old alert target may have been destroyed while this sensor was disabled.
   auto target=reinterpret_cast<Byte*>(U(i->actor,securityFields[3].offset));
   if(!Alive(target)){
    auto table=reinterpret_cast<Byte**>(objectArray[0]);
    for(DWORD j=0;j<objectArray[1];++j)if(table[j]&&U(table[j],0x20)==state&&IsA(table[j],L"State")&&!_wcsicmp(name(table[j]),L"s_Alert")){state=securityPatrol;break;}
   }
   SecurityQuietGo(i->actor,state);
  }
  i=securityStates.erase(i);
 }
 nextSecurity=0;
}
static void SecurityResolve(){
 auto core=GetModuleHandleW(L"Core.dll");securityGo=reinterpret_cast<int (__thiscall*)(void*,DWORD)>(GetProcAddress(core,"?GotoState@UObject@@UAE?AW4EGotoState@@VFName@@@Z"));
 auto text=reinterpret_cast<const wchar_t* (__thiscall*)(void*)>(GetProcAddress(core,"??DFName@@QBEPBGXZ"));
 for(auto& f:securityFields){f.offset=0;f.mask=0;f.count=0;}
 securityDisabled=securityPatrol=0;securityAllPawns=-1;
 auto table=reinterpret_cast<Byte**>(objectArray[0]);
 for(DWORD i=0;i<objectArray[1];++i){auto obj=table[i];if(!Alive(obj))continue;auto owner=reinterpret_cast<Byte*>(U(obj,0x18));auto type=reinterpret_cast<Byte*>(U(obj,0x24));if(!Alive(owner)||!Alive(type))continue;
  for(auto& f:securityFields)if(!wcscmp(name(owner),f.owner)&&!wcscmp(name(obj),f.name)&&!wcscmp(name(type),f.type)){
   f.offset=U(obj,0x44);f.mask=U(obj,0x54);f.count=*reinterpret_cast<WORD*>(obj+0x38);
  }
  if(!wcscmp(name(owner),L"ESensor")&&IsA(obj,L"State")){
   if(!_wcsicmp(name(obj),L"s_Deactivated"))securityDisabled=U(obj,0x20);
   if(!_wcsicmp(name(obj),L"s_Patrol"))securityPatrol=U(obj,0x20);
  }
  if(text&&IsA(obj,L"Enum")&&!wcscmp(name(obj),L"EScannedActorType")){
   auto values=reinterpret_cast<DWORD*>(U(obj,0x38));DWORD count=U(obj,0x3c);if(values&&count<=256)for(DWORD j=0;j<count;++j)if(!_wcsicmp(text(values+j),L"SCAN_AllPawnsAndChangedActors"))securityAllPawns=static_cast<int>(j);
  }
 }
 if(!securityGo||!securityDisabled||!securityPatrol||securityAllPawns<0)throw 1;
 for(const auto& f:securityFields)if(f.offset<0x28||f.offset>0x8000||f.count!=1)throw 1;
 if(!securityFields[1].mask||(securityFields[1].mask&(securityFields[1].mask-1)))throw 1;
 nextSecurity=0;
}
static void SecurityTick(){
 auto now=GetTickCount64();if(now<nextSecurity)return;nextSecurity=now+100;
 DWORD count=objectArray[1];if(count>1000000)throw 1;
 for(DWORD i=0;i<count;++i){auto actor=reinterpret_cast<Byte**>(objectArray[0])[i];if(!IsA(actor,L"ESensor")||(U(actor,0x2c)&0x10)||U(actor,0x6c)!=U(player,0x6c)||SecurityDestroyed(actor))continue;
  bool camera=IsA(actor,L"EBaseCam"),turret=IsA(actor,L"ETurret");if(!camera&&!turret)continue;
  DWORD group=camera?CamerasOff:TurretsOff;
  if((activeMask&group)&&SecurityPolicy::Target(camera,turret,group)){
   auto state=SecurityState(actor);if(state&&U(state,0x20)!=securityDisabled){
    auto found=std::find_if(securityStates.begin(),securityStates.end(),[&](const SecurityRef& r){return r.actor==actor&&r.index==U(actor,4)&&r.objectName==U(actor,0x20);});
    if(found==securityStates.end())securityStates.push_back({actor,U(actor,4),U(actor,0x20),U(actor,0x6c),group,U(state,0x20)});
    SecurityQuietGo(actor,securityDisabled);
   }
  }
  if((activeMask&IffOff)&&SecurityPolicy::Target(camera,turret,IffOff)){
   auto found=std::find_if(securityIff.begin(),securityIff.end(),[&](const SecurityRef& r){return r.actor==actor&&r.index==U(actor,4)&&r.objectName==U(actor,0x20);});
   if(found==securityIff.end())securityIff.push_back({actor,U(actor,4),U(actor,0x20),U(actor,0x6c),IffOff,*(actor+securityFields[2].offset)});
   *(actor+securityFields[2].offset)=static_cast<Byte>(securityAllPawns);
  }
 }
}
