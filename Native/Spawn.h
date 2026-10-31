#pragma once
#include <cmath>

// Prototype: one ordinary guard of a class already present in the current map.
namespace TrainerSpawn {
using Byte=unsigned char;
struct Vec {float x,y,z;};
struct Rot {int pitch,yaw,roll;};
using SpawnFn=Byte* (__thiscall*)(void*,void*,DWORD,Vec,Rot,void*,int,int,void*,void*);
using DestroyFn=int (__thiscall*)(void*,void*,int);
using EventFn=void (__thiscall*)(void*,void*,void*,void*);
using FindFn=void* (__thiscall*)(void*,DWORD,int);
using AddFn=int (__thiscall*)(void*,int,int);
struct SpawnRecord {DWORD version;Byte *pawn,*controller,*level;DWORD pawnIndex,controllerIndex,levelIndex,pawnName;};
static SpawnRecord* record=nullptr;
static HANDLE recordHandle=nullptr;
static bool SharedRecord() {
    if(record)return record->version==1;
    wchar_t key[96];swprintf_s(key,L"Local\\SCTrainer.SpawnRecord.%lu",GetCurrentProcessId());
    recordHandle=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(SpawnRecord),key);
    if(!recordHandle)return false;
    bool exists=GetLastError()==ERROR_ALREADY_EXISTS;
    record=static_cast<SpawnRecord*>(MapViewOfFile(recordHandle,FILE_MAP_ALL_ACCESS,0,0,sizeof(SpawnRecord)));
    if(!record){CloseHandle(recordHandle);recordHandle=nullptr;return false;}
    if(!exists){ZeroMemory(record,sizeof(SpawnRecord));record->version=1;}
    return record->version==1;
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
static bool Process(Request* r,void* view,HMODULE core,HMODULE engine,DWORD* objects) {
    bool remove=!wcscmp(r->map,L"--npc-remove");
    bool status=!wcscmp(r->map,L"--npc-status");
    if(!remove && !status && wcscmp(r->map,L"--npc-spawn"))return false;
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
    if(!TrainerCheats::IsA(player,L"EPlayerController")||!package||!_wcsicmp(name(package),L"menu")){r->result=16;return true;}
    Byte* level=nullptr;
    DWORD possessName=0;
    for(DWORD i=0;i<count;++i) {
        auto obj=table[i];if(!obj)continue;
        auto cls=reinterpret_cast<void*>(U(obj,0x24));
        if(cls && !wcscmp(name(cls),L"Level")&&reinterpret_cast<Byte*>(U(obj,0x18))==package)level=obj;
        if(!wcscmp(name(obj),L"Possess")&&cls&&!wcscmp(name(cls),L"Function"))possessName=U(obj,0x20);
    }
    if(!level||!possessName){r->result=3;return true;}
    bool tracked=record->pawn && record->level==level && TrainerCheats::Alive(record->level,record->levelIndex) &&
        TrainerCheats::Alive(record->pawn,record->pawnIndex) && U(record->pawn,0x20)==record->pawnName && !(U(record->pawn,0x2c)&0x10);
    if(!tracked){record->pawn=nullptr;record->controller=nullptr;}
    if(status){wcscpy_s(r->map,tracked?L"active":L"none");r->result=29;return true;}
    if(GetForegroundWindow()!=r->window || IsIconic(r->window)){r->result=14;return true;}
    if(remove) {
        if(!tracked){r->result=22;return true;}
        DWORD lifeSpan=Property(objects,name,L"Actor",L"LifeSpan");
        if(!lifeSpan){r->result=3;return true;}
        // Do not call DestroyActor from the window hook. Expire the pawn during the
        // engine's normal actor tick; Pawn.Destroyed -> Controller.PawnDied handles AI cleanup.
        *reinterpret_cast<float*>(record->pawn+lifeSpan)=0.1f;
        r->result=28;return true;
    }
    if(record->pawn&&record->level==level&&(TrainerCheats::Alive(record->pawn,record->pawnIndex)&&U(record->pawn,0x20)==record->pawnName)){r->result=23;return true;}
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
    record->controller=ctrl;record->controllerIndex=U(ctrl,4);
    auto possess=find(ctrl,possessName,0);
    if(!possess){destroy(level,created,0);if(TrainerCheats::Alive(ctrl,record->controllerIndex))destroy(level,ctrl,0);record->pawn=nullptr;record->controller=nullptr;r->result=26;return true;}
    struct {Byte* pawn;} parameters={created};
    process(ctrl,possess,&parameters,nullptr);
    if(reinterpret_cast<Byte*>(U(created,controllerOffset))!=ctrl) {
        destroy(level,created,0);if(TrainerCheats::Alive(ctrl,record->controllerIndex))destroy(level,ctrl,0);
        record->pawn=nullptr;record->controller=nullptr;r->result=26;return true;
    }
    swprintf_s(r->map,L"%s at %08lX (AI %08lX)",name(reinterpret_cast<void*>(U(created,0x24))),reinterpret_cast<DWORD>(created),reinterpret_cast<DWORD>(ctrl));
    r->result=21;return true;
}
static bool SafeRequest(Request* r,void* view,HMODULE core,HMODULE engine,DWORD* objects) {
    try{return Process(r,view,core,engine,objects);}
    catch(...){r->result=7;return true;}
}
}
