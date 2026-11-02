#pragma once
namespace TrainerEquipment {
using namespace TrainerCheats;
using TrainerSpawn::Property;
static const wchar_t* classes[]={L"EFn7",L"EF2000",L"EStickyShocker",L"ERingAirfoilRound",L"EStickyCamera",L"EDiversionCamera",L"ESmokeGrenade",L"EWallMine",L"EFragGrenade",L"ELockpick",L"EDisposablePick",L"EOpticCable",L"ECameraJammer",L"ELaserMic",L"EMedKit",L"EFlare",L"EChemFlare"};
static Byte* FindItem(Byte* inventory,DWORD offset,int id) {
    auto cats=*reinterpret_cast<Array*>(inventory+offset);
    if(!cats.data||cats.count<0||cats.count>16||cats.capacity<cats.count)return nullptr;
    for(int c=0;c<cats.count;++c){auto items=reinterpret_cast<Array*>(cats.data)[c];
        if(items.count<0||items.count>128||items.capacity<items.count||(!items.data&&items.count))return nullptr;
        for(int i=0;i<items.count;++i){auto item=reinterpret_cast<Byte**>(items.data)[i];
            if(IsA(item,L"EInventoryItem")&&!(U(item,0x2c)&0x10)&&!wcscmp(name(reinterpret_cast<Byte*>(U(item,0x24))),classes[id-1]))return item;
        }
    }
    return nullptr;
}
static bool Handle(Request* r,void* view,HMODULE core,HMODULE engine,DWORD* objects) {
    bool list=!wcscmp(r->map,L"--equipment-list");int id=0,op=0,quantity=0;wchar_t extra=0;
    if(!list&&wcsncmp(r->map,L"--equipment:",12))return false;
    if(!list&&(swscanf_s(r->map,L"--equipment:%d:%d:%d%c",&id,&op,&quantity,&extra,1)!=3||id<1||id>17||op<0||op>2||
        (op!=2&&(quantity<1||quantity>999))||(op==2&&quantity!=0))){r->result=6;return true;}
    objectArray=objects;name=reinterpret_cast<NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));
    auto find=reinterpret_cast<TrainerSpawn::FindFn>(GetProcAddress(core,"?FindFunction@UObject@@QAEPAVUFunction@@VFName@@H@Z"));
    auto event=reinterpret_cast<TrainerSpawn::EventFn>(GetProcAddress(core,"?ProcessEvent@UObject@@UAEXPAVUFunction@@PAX1@Z"));
    if(!name||!find||!event){r->result=52;return true;}
    auto pc=reinterpret_cast<Byte*>(U(static_cast<Byte*>(view),0x34));
    auto info=Alive(pc)?reinterpret_cast<Byte*>(U(pc,0x6c)):nullptr;
    auto package=Alive(info)?reinterpret_cast<Byte*>(U(info,0x18)):nullptr;
    DWORD pawnField=Property(objects,name,L"EPlayerController",L"EPawn"),inventoryField=Property(objects,name,L"EPawn",L"FullInventory"),
        itemsField=Property(objects,name,L"EInventory",L"InventoryList"),q=Property(objects,name,L"EInventoryItem",L"Quantity"),
        maximum=Property(objects,name,L"EInventoryItem",L"MaxQuantity");
    if(!pawnField||!inventoryField||!itemsField||!q||!maximum){r->result=52;return true;}
    if(!IsA(pc,L"EPlayerController")||!Alive(package)||!_wcsicmp(name(package),L"menu")){r->result=16;return true;}
    auto sam=reinterpret_cast<Byte*>(U(pc,pawnField));
    auto inventory=IsA(sam,L"EPawn")?reinterpret_cast<Byte*>(U(sam,inventoryField)):nullptr;
    if(!IsA(inventory,L"EInventory")){r->result=16;return true;}
    if(list){
        wchar_t key[96];EquipmentSnapshotName(key,GetCurrentProcessId());auto mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,key);
        if(!mapping){r->result=7;return true;}
        auto data=static_cast<EquipmentSnapshot*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(EquipmentSnapshot)));
        if(!data){CloseHandle(mapping);r->result=7;return true;}
        ZeroMemory(data,sizeof(*data));data->version=1;data->count=17;
        for(int i=1;i<=17;++i){auto item=FindItem(inventory,itemsField,i);auto& e=data->entries[i-1];e.id=i;e.owned=item?1:0;
            if(item){e.quantity=static_cast<int>(U(item,q));e.maximum=static_cast<int>(U(item,maximum));}}
        UnmapViewOfFile(data);CloseHandle(mapping);r->result=50;return true;
    }
    if(GetForegroundWindow()!=r->window||IsIconic(r->window)){r->result=14;return true;}
    if(wcscmp(TrainerSpawn::State(pc,name),L"s_PlayerWalking")){r->result=56;return true;}
    auto item=FindItem(inventory,itemsField,id);
    if(op==2){
        if(!item||static_cast<int>(U(item,q))<=0){r->result=53;return true;}
        if(id>=3&&id<=7&&!FindItem(inventory,itemsField,2)){r->result=54;return true;}
        auto select=TrainerSpawn::Function(inventory,L"SetSelectedItem",objects,name,find);
        auto can=TrainerSpawn::Function(pc,L"CanSwitchToHandItem",objects,name,find);
        if(!select||!can||!TrainerSpawn::Param(select,L"Item",0,4,objects,name)||!TrainerSpawn::Param(can,L"Item",0,4,objects,name)||
            !TrainerSpawn::Param(can,L"ReturnValue",4,4,objects,name)){r->result=52;return true;}
        struct {Byte* item;DWORD allowed;} check={item,0};event(pc,can,&check,nullptr);
        if(!check.allowed){r->result=56;return true;}
        struct {Byte* item;} params={item};event(inventory,select,&params,nullptr);
        swprintf_s(r->map,L"Selected %s.",classes[id-1]);r->result=51;return true;
    }
    if(!item){
        auto spawn=reinterpret_cast<TrainerSpawn::SpawnFn>(GetProcAddress(engine,"?SpawnActor@ULevel@@UAEPAVAActor@@PAVUClass@@VFName@@VFVector@@VFRotator@@PAV2@HH4PAVAPawn@@@Z"));
        auto load=reinterpret_cast<Byte* (__cdecl*)(void*,void*,const wchar_t*,const wchar_t*,DWORD,void*)>(GetProcAddress(core,"?StaticLoadClass@UObject@@SAPAVUClass@@PAV2@PAV1@PBG2KPAX@Z"));
        if(!spawn||!load){r->result=52;return true;}
        Byte *base=nullptr,*level=nullptr;auto table=reinterpret_cast<Byte**>(objects[0]);
        for(DWORD i=0;i<objects[1];++i){auto obj=table[i];if(!Alive(obj))continue;
            if(IsA(obj,L"Class")&&!wcscmp(name(obj),L"EInventoryItem"))base=obj;
            if(IsA(obj,L"Level")&&reinterpret_cast<Byte*>(U(obj,0x18))==package)level=obj;
        }
        if(!base||!level){r->result=52;return true;}
        wchar_t classPath[96];swprintf_s(classPath,L"EchelonIngredient.%s",classes[id-1]);
        auto cls=load(base,nullptr,classPath,nullptr,2,nullptr);
        if(!cls){r->result=52;return true;}
        auto at=*reinterpret_cast<TrainerSpawn::Vec*>(sam+0xd4);auto rotation=*reinterpret_cast<TrainerSpawn::Rot*>(pc+0xe0);
        auto created=spawn(level,cls,0,at,rotation,sam,0,1,nullptr,nullptr);
        if(!IsA(created,L"EInventoryItem")){r->result=52;return true;}
        auto add=TrainerSpawn::Function(created,L"Add",objects,name,find);
        if(!add||!TrainerSpawn::Param(add,L"NewOwner",0,4,objects,name)||!TrainerSpawn::Param(add,L"NewController",4,4,objects,name)||
            !TrainerSpawn::Param(add,L"Inventory",8,4,objects,name)){
            DWORD lifespan=Property(objects,name,L"Actor",L"LifeSpan");if(lifespan)*reinterpret_cast<float*>(created+lifespan)=0.01f;
            r->result=52;return true;
        }
        int limit=static_cast<int>(U(created,maximum));
        if(limit<1||limit>10000){DWORD lifespan=Property(objects,name,L"Actor",L"LifeSpan");if(lifespan)*reinterpret_cast<float*>(created+lifespan)=0.01f;r->result=52;return true;}
        *reinterpret_cast<int*>(created+q)=(quantity<limit?quantity:limit);
        struct {Byte* owner;Byte* controller;Byte* inventory;} args={sam,pc,inventory};event(created,add,&args,nullptr);
        item=FindItem(inventory,itemsField,id);
        if(!item){
            if(Alive(created)&&!(U(created,0x2c)&0x10)){
                DWORD lifespan=Property(objects,name,L"Actor",L"LifeSpan");if(lifespan)*reinterpret_cast<float*>(created+lifespan)=0.01f;
            }
            r->result=52;return true;
        }
    }else{
        int limit=static_cast<int>(U(item,maximum)),current=static_cast<int>(U(item,q));
        if(limit<1||limit>10000||current<0||current>10000){r->result=52;return true;}
        int value=op==0?current+quantity:quantity;
        *reinterpret_cast<int*>(item+q)=value<limit?value:limit;
    }
    swprintf_s(r->map,L"%s: %d / %d (game limit).",classes[id-1],static_cast<int>(U(item,q)),static_cast<int>(U(item,maximum)));
    r->result=51;return true;
}
}
