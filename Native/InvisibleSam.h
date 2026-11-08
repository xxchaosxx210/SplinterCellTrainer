#pragma once
// Included within TrainerCheats. All changes run on the game window thread.
using PerceptionEventFn=void (__thiscall*)(void*,void*,void*,void*);
static PerceptionEventFn perceptionEvent=nullptr;
struct PerceptionSlot {void** address;void* original;};
static std::vector<PerceptionSlot> perceptionSlots;
static size_t perceptionSlotIndex=0;
static ULONGLONG nextPerceptionScan=0;
static Field perceptionFields[]={
    {L"Actor",L"HeatIntensity",L"FloatProperty"},
    {L"EPawn",L"SmellRadius",L"FloatProperty"}
};

static bool PerceptionObserver(Byte* actor) {
    return actor!=pawn&&(IsA(actor,L"EAIController")||IsA(actor,L"ESensor")||
        IsA(actor,L"EPattern")||IsA(actor,L"EGroupAI")||IsA(actor,L"EAIPawn")||IsA(actor,L"EDog"));
}

static void __fastcall InvisiblePerceptionEvent(Byte* actor,void*,void* function,void* parameters,void* result) {
    Byte* sam=pawn;
    const bool filter=(activeMask&InvisibleSam)&&Alive(sam)&&PerceptionObserver(actor);
    if(!filter){perceptionEvent(actor,function,parameters,result);return;}
    const DWORD samIndex=U(sam,4);
    const DWORD mask=fields[26].mask;
    auto flags=reinterpret_cast<volatile LONG*>(sam+fields[26].offset);
    const LONG previous=InterlockedOr(flags,mask)&mask;
    __try {
        perceptionEvent(actor,function,parameters,result);
    } __finally {
        if(Alive(sam,samIndex)){
            if(previous)InterlockedOr(flags,mask);
            else InterlockedAnd(flags,~mask);
        }
    }
}

static void PerceptionRestore() {
    for(const auto& record:perceptionSlots){
        auto slot=record.address;
        if(*slot!=reinterpret_cast<void*>(&InvisiblePerceptionEvent))continue;
        DWORD protection=0;
        if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&protection))continue;
        InterlockedCompareExchangePointer(slot,record.original,
            reinterpret_cast<void*>(&InvisiblePerceptionEvent));
        DWORD ignored=0;
        VirtualProtect(slot,sizeof(void*),protection,&ignored);
    }
    perceptionSlots.clear();
    nextPerceptionScan=0;
}

static void* PerceptionEntry(void* address) {
    // Imported virtual methods can use a module-local jump thunk.
    auto code=static_cast<Byte*>(address);
    for(int depth=0;depth<3;++depth){
        if(code[0]==0xff&&code[1]==0x25){
            auto importSlot=*reinterpret_cast<void***>(code+2);
            code=static_cast<Byte*>(*importSlot);
        } else if(code[0]==0xe9){
            code+=5+*reinterpret_cast<int*>(code+1);
        } else break;
    }
    return code;
}

static void PerceptionInstall(Byte* actor) {
    auto table=*reinterpret_cast<void***>(actor);
    auto slot=table+perceptionSlotIndex;
    if(std::any_of(perceptionSlots.begin(),perceptionSlots.end(),[slot](const PerceptionSlot& record){return record.address==slot;}))return;
    auto original=*slot;
    if(PerceptionEntry(original)!=PerceptionEntry(reinterpret_cast<void*>(perceptionEvent)))throw 1;
    perceptionSlots.push_back({slot,original});
    DWORD protection=0;
    if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&protection))throw 1;
    auto previous=InterlockedCompareExchangePointer(slot,reinterpret_cast<void*>(&InvisiblePerceptionEvent),
        original);
    DWORD ignored=0;
    VirtualProtect(slot,sizeof(void*),protection,&ignored);
    if(previous!=original)throw 1;
}

static void PerceptionResolve() {
    auto core=GetModuleHandleW(L"Core.dll");
    auto baseEvent=GetProcAddress(core,"?ProcessEvent@UObject@@UAEXPAVUFunction@@PAX1@Z");
    perceptionEvent=reinterpret_cast<PerceptionEventFn>(GetProcAddress(GetModuleHandleW(L"Engine.dll"),
        "?ProcessEvent@AActor@@UAEXPAVUFunction@@PAX1@Z"));
    auto baseTable=reinterpret_cast<void**>(GetProcAddress(core,"??_7UObject@@6B@"));
    if(!baseEvent||!perceptionEvent||!baseTable)throw 1;
    bool found=false;
    for(size_t i=0;i<128;++i){
        if(baseTable[i]!=reinterpret_cast<void*>(baseEvent))continue;
        perceptionSlotIndex=i;found=true;break;
    }
    if(!found)throw 1;
    for(auto& field:perceptionFields){field.offset=0;field.count=0;}
    auto table=reinterpret_cast<Byte**>(objectArray[0]);
    for(DWORD i=0;i<objectArray[1];++i){
        auto object=table[i];
        if(!Alive(object))continue;
        auto owner=reinterpret_cast<Byte*>(U(object,0x18));
        auto type=reinterpret_cast<Byte*>(U(object,0x24));
        if(!Alive(owner)||!Alive(type))continue;
        for(auto& field:perceptionFields){
            if(wcscmp(name(owner),field.owner)||wcscmp(name(object),field.name)||wcscmp(name(type),field.type))continue;
            field.offset=U(object,0x44);field.count=*reinterpret_cast<WORD*>(object+0x38);
        }
    }
    for(const auto& field:perceptionFields)
        if(field.offset<0x28||field.offset>0x8000||field.count!=1)throw 1;
    nextPerceptionScan=0;
}

static void PerceptionTick() {
    // Heat is used by turrets. Dogs use SmellRadius to locate Sam's scent points.
    Scale(pawn,perceptionFields[0].offset,0,InvisibleSam);
    const auto now=GetTickCount64();
    if(now<nextPerceptionScan)return;
    nextPerceptionScan=now+250;
    auto table=reinterpret_cast<Byte**>(objectArray[0]);
    for(DWORD i=0;i<objectArray[1];++i){
        auto actor=table[i];
        if(!PerceptionObserver(actor)||(U(actor,0x2c)&0x10)||U(actor,0x6c)!=U(player,0x6c))continue;
        PerceptionInstall(actor);
        if(IsA(actor,L"EDog"))Scale(actor,perceptionFields[1].offset,0,InvisibleSam);
    }
}
