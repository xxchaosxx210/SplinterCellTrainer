#pragma once
// One-shot, game-thread deactivation using the same state as scripted camera shutdown.
namespace TrainerCameras {
using namespace TrainerCheats;
static bool Handle(Request* r,void* view,HMODULE core,DWORD* objects) {
    if(wcscmp(r->map,L"--cameras-disable"))return false;
    objectArray=objects;
    name=reinterpret_cast<NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));
    auto go=reinterpret_cast<int (__thiscall*)(void*,DWORD)>(GetProcAddress(core,"?GotoState@UObject@@UAE?AW4EGotoState@@VFName@@@Z"));
    if(!name||!go){r->result=47;return true;}
    auto controller=reinterpret_cast<Byte*>(U(static_cast<Byte*>(view),0x34));
    auto info=Alive(controller)?reinterpret_cast<Byte*>(U(controller,0x6c)):nullptr;
    auto package=Alive(info)?reinterpret_cast<Byte*>(U(info,0x18)):nullptr;
    if(!IsA(controller,L"EPlayerController")||!Alive(package)||!_wcsicmp(name(package),L"menu")){r->result=16;return true;}
    Field f[]={ {L"ESensor",L"SoundDisable",L"ObjectProperty"}, {L"EGameplayObject",L"ChangeListWhenDamaged",L"BoolProperty"} };
    DWORD state=0;
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i){
        auto obj=table[i];if(!Alive(obj))continue;
        auto owner=reinterpret_cast<Byte*>(U(obj,0x18));
        auto type=reinterpret_cast<Byte*>(U(obj,0x24));
        if(!Alive(owner)||!Alive(type))continue;
        if(!wcscmp(name(owner),L"ESensor")&&!wcscmp(name(type),L"State")&&!wcscmp(name(obj),L"s_Deactivated"))state=U(obj,0x20);
        for(auto& field:f)if(!wcscmp(name(owner),field.owner)&&!wcscmp(name(obj),field.name)&&!wcscmp(name(type),field.type)){
            field.offset=U(obj,0x44);field.mask=U(obj,0x54);
            if(*reinterpret_cast<WORD*>(obj+0x38)!=1||*reinterpret_cast<WORD*>(obj+0x3a)!=4)field.offset=0;
        }
    }
    if(!state||f[0].offset<0x28||f[0].offset>0x8000||f[1].offset<0x28||f[1].offset>0x8000||!f[1].mask){r->result=47;return true;}
    DWORD changed=0,already=0;
    const DWORD count=objects[1];
    for(DWORD i=0;i<count;++i){
        auto camera=table[i];
        if(!IsA(camera,L"EBaseCam")||U(camera,0x6c)!=reinterpret_cast<DWORD>(info)||(U(camera,0x2c)&0x10))continue;
        auto current=TrainerSpawn::State(camera,name);
        if(!_wcsicmp(current,L"s_Deactivated")){++already;continue;}
        if(!_wcsicmp(current,L"s_Destructed")||!_wcsicmp(current,L"s_Jammed"))continue;
        auto sound=reinterpret_cast<DWORD*>(camera+f[0].offset);
        auto change=reinterpret_cast<DWORD*>(camera+f[1].offset);
        DWORD originalSound=*sound,originalChange=*change&f[1].mask;
        // Do not emit the disable sound or an AI disabled-turret change notification.
        __try { *sound=0;*change&=~f[1].mask;go(camera,state); }
        __finally { *sound=originalSound;*change=(*change&~f[1].mask)|originalChange; }
        if(!_wcsicmp(TrainerSpawn::State(camera,name),L"s_Deactivated"))++changed;
    }
    swprintf_s(r->map,L"Disabled %lu security cameras (%lu already disabled).",changed,already);
    r->result=46;return true;
}
}
