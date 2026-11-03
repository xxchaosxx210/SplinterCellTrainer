#pragma once
#include "ObjectiveRoutes.h"
namespace TrainerObjectives {
using namespace TrainerCheats;
static Byte* lastPattern=nullptr;static DWORD lastPatternIndex=0,lastPlayerIndex=0;
static bool Handle(Request* r,void* view,HMODULE core,DWORD* objects){
 bool inspect=!wcscmp(r->map,L"--objective-next");
 if(!inspect&&wcscmp(r->map,L"--objective-complete"))return false;
 objectArray=objects;name=reinterpret_cast<NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));
 auto find=reinterpret_cast<TrainerSpawn::FindFn>(GetProcAddress(core,"?FindFunction@UObject@@QAEPAVUFunction@@VFName@@H@Z"));
 auto event=reinterpret_cast<TrainerSpawn::EventFn>(GetProcAddress(core,"?ProcessEvent@UObject@@UAEXPAVUFunction@@PAX1@Z"));
 auto text=reinterpret_cast<const wchar_t* (__thiscall*)(void*)>(GetProcAddress(core,"??DFName@@QBEPBGXZ"));
 if(!name||!find||!event||!text){r->result=68;return true;}
 auto pc=reinterpret_cast<Byte*>(U(static_cast<Byte*>(view),0x34));
 auto info=Alive(pc)?reinterpret_cast<Byte*>(U(pc,0x6c)):nullptr;
 auto package=Alive(info)?reinterpret_cast<Byte*>(U(info,0x18)):nullptr;
 if(!IsA(pc,L"EPlayerController")||!Alive(package)||!_wcsicmp(name(package),L"menu")){r->result=16;return true;}
 Field fields[]={
 {L"EPlayerController",L"GoalList",L"ObjectProperty"},
 {L"ESList",L"FirstNode",L"ObjectProperty"},
 {L"EListNode",L"NextNode",L"ObjectProperty"},
 {L"EListNode",L"Data",L"ObjectProperty"},
 {L"ENote",L"ID",L"NameProperty"},
 {L"ENote",L"bCompleted",L"BoolProperty"}};
 auto table=reinterpret_cast<Byte**>(objects[0]);
 for(DWORD i=0;i<objects[1];++i){auto obj=table[i];if(!Alive(obj))continue;
  auto owner=reinterpret_cast<Byte*>(U(obj,0x18));auto type=reinterpret_cast<Byte*>(U(obj,0x24));if(!Alive(owner)||!Alive(type))continue;
  for(auto& f:fields)if(!wcscmp(name(owner),f.owner)&&!wcscmp(name(obj),f.name)&&!wcscmp(name(type),f.type)){
   f.offset=U(obj,0x44);f.mask=U(obj,0x54);f.count=*reinterpret_cast<WORD*>(obj+0x38);
  }
 }
 for(const auto& f:fields)if(f.offset<0x28||f.offset>0x8000||f.count!=1){r->result=68;return true;}
 if(!fields[5].mask||(fields[5].mask&(fields[5].mask-1))){r->result=68;return true;}
 auto list=reinterpret_cast<Byte*>(U(pc,fields[0].offset));
 if(!IsA(list,L"ESList")){r->result=68;return true;}
 auto node=reinterpret_cast<Byte*>(U(list,fields[1].offset));Byte* selected=nullptr;
 // Validate the entire list before changing anything, including cyclic/corrupt lists.
 std::vector<Byte*> visited;
 while(node){
  if(!IsA(node,L"EListNode")||visited.size()>=256||std::find(visited.begin(),visited.end(),node)!=visited.end()){r->result=68;return true;}
  visited.push_back(node);auto note=reinterpret_cast<Byte*>(U(node,fields[3].offset));
  if(!IsA(note,L"ENote")){r->result=68;return true;}
  if(!selected&&!(U(note,fields[5].offset)&fields[5].mask))selected=note;
  node=reinterpret_cast<Byte*>(U(node,fields[2].offset));
 }
 if(!selected){wcscpy_s(r->map,L"No pending objectives in the current mission.");r->result=69;return true;}
 DWORD id=U(selected,fields[4].offset);wchar_t label[80];wcsncpy_s(label,text(&id),_TRUNCATE);
 auto route=ObjectiveRoutes::Find(name(package),label);
 if(!route){swprintf_s(r->map,L"No scripted completion mapped for %s / %s. Objective unchanged.",name(package),label);r->result=71;return true;}
 auto classOffset=TrainerSpawn::Property(objects,name,L"EGroupAI",L"ScriptedPatternClass");
 auto patternOffset=TrainerSpawn::Property(objects,name,L"EGroupAI",L"ScriptedPattern");
 if(!classOffset||!patternOffset){r->result=68;return true;}
 Byte* group=nullptr;unsigned matches=0;
 for(DWORD i=0;i<objects[1];++i){auto obj=table[i];if(!IsA(obj,L"EGroupAI")||(U(obj,0x2c)&0x10)||U(obj,0x6c)!=reinterpret_cast<DWORD>(info))continue;
  auto cls=reinterpret_cast<Byte*>(U(obj,classOffset));if(IsA(cls,L"Class")&&!wcscmp(name(cls),route->pattern)){group=obj;++matches;}
 }
 if(matches!=1){r->result=68;return true;}
 auto fn=TrainerSpawn::Function(group,L"SendJumpEvent",objects,name,find);
 auto makeName=reinterpret_cast<void (__thiscall*)(void*,const wchar_t*,int)>(GetProcAddress(core,"??0FName@@QAE@PBGW4EFindName@@@Z"));
 DWORD jump=0;if(makeName)makeName(&jump,route->label,0); // FNAME_Find: never add guessed script labels.
 if(!fn||!jump||!TrainerSpawn::Param(fn,L"LabelName",0,4,objects,name)||!TrainerSpawn::Param(fn,L"bAffectLastZone",4,4,objects,name)||!TrainerSpawn::Param(fn,L"bForceJump",8,4,objects,name)){r->result=68;return true;}
 if(inspect){swprintf_s(r->map,L"Next: %s; script: %s",label,route->label);r->result=69;return true;}
 if(GetForegroundWindow()!=r->window||IsIconic(r->window)||wcscmp(TrainerSpawn::State(pc,name),L"s_PlayerWalking")){r->result=70;return true;}
 // Do not interrupt or restart an objective sequence that is still running.
 if(lastPlayerIndex==U(pc,4)&&Alive(lastPattern,lastPatternIndex)&&U(lastPattern,0x6c)==reinterpret_cast<DWORD>(info)&&!_wcsicmp(TrainerSpawn::State(lastPattern,name),L"Pattern")){
  wcscpy_s(r->map,L"The previous objective script is still running. Wait for dialogue/events to finish.");r->result=69;return true;
 }
 // Let EGroupAI initialize its owned pattern and execute the normal mission event.
 struct {DWORD label,affectLastZone,force;} params={jump,0,0};event(group,fn,&params,nullptr);
 auto pattern=reinterpret_cast<Byte*>(U(group,patternOffset));
 if(!IsA(pattern,route->pattern)){r->result=68;return true;}
 lastPattern=pattern;lastPatternIndex=U(pattern,4);lastPlayerIndex=U(pc,4);
 swprintf_s(r->map,L"Started objective script: %s. Let the game run to finish it.",label);r->result=69;return true;
}
}
