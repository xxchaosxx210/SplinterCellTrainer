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

static Byte* perceptionObserverClasses[6]={};
static const wchar_t* perceptionObserverNames[]={L"EAIController",L"ESensor",L"EPattern",L"EGroupAI",L"EAIPawn",L"EDog"};
static DWORD perceptionThread=0,perceptionPawnIndex=0;
static volatile int* perceptionExitRequested=nullptr;
static DWORD perceptionCursor=0;
static bool initialPerceptionScan=true;

static void PerceptionRememberClass(Byte* object) {
    if(!IsA(object,L"Class"))return;
    for(size_t i=0;i<6;++i){
        if(!perceptionObserverClasses[i]&&!wcscmp(name(object),perceptionObserverNames[i]))
            perceptionObserverClasses[i]=object;
    }
}

static bool PerceptionObserver(Byte* actor) {
    if(actor==pawn||!Alive(actor))return false;
    auto type=reinterpret_cast<Byte*>(U(actor,0x24));
    for(int depth=0;type&&depth<24;++depth){
        for(auto expected:perceptionObserverClasses)
            if(expected&&type==expected)return true;
        type=reinterpret_cast<Byte*>(U(type,0x2c));
    }
    return false;
}

static bool PerceptionDog(Byte* actor) {
    auto type=reinterpret_cast<Byte*>(U(actor,0x24));
    for(int depth=0;type&&depth<24;++depth){
        if(perceptionObserverClasses[5]&&type==perceptionObserverClasses[5])return true;
        type=reinterpret_cast<Byte*>(U(type,0x2c));
    }
    return false;
}

static bool PerceptionCanFilter(Byte* actor,Byte* sam) {
    __try {
        if(perceptionExitRequested&&*perceptionExitRequested)return false;
        // Validate the table entry before dereferencing a pawn that may be freed.
        if(!(activeMask&InvisibleSam)||!Alive(sam,perceptionPawnIndex))return false;
        if(U(sam,0x2c)&0x10)return false;
        return PerceptionObserver(actor);
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        InterlockedAnd(&activeMask,~static_cast<LONG>(InvisibleSam));
        return false;
    }
}

static void PerceptionRestoreHidden(Byte* sam,DWORD index,DWORD mask,LONG previous) {
    __try {
        if(perceptionExitRequested&&*perceptionExitRequested)return;
        if(!Alive(sam,index))return;
        auto flags=reinterpret_cast<volatile LONG*>(sam+fields[26].offset);
        if(previous)InterlockedOr(flags,mask);
        else InterlockedAnd(flags,~mask);
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        InterlockedAnd(&activeMask,~static_cast<LONG>(InvisibleSam));
    }
}

static void __fastcall InvisiblePerceptionEvent(Byte* actor,void*,void* function,void* parameters,void* result) {
    Byte* sam=pawn;
    if(GetCurrentThreadId()!=perceptionThread||!PerceptionCanFilter(actor,sam)){
        perceptionEvent(actor,function,parameters,result);return;
    }
    const DWORD samIndex=perceptionPawnIndex;
    const DWORD mask=fields[26].mask;
    auto flags=reinterpret_cast<volatile LONG*>(sam+fields[26].offset);
    const LONG previous=InterlockedOr(flags,mask)&mask;
    __try {
        perceptionEvent(actor,function,parameters,result);
    } __finally {
        PerceptionRestoreHidden(sam,samIndex,mask,previous);
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
    perceptionCursor=0;
    initialPerceptionScan=true;
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
    perceptionThread=GetCurrentThreadId();
    for(auto& observer:perceptionObserverClasses)observer=nullptr;
    perceptionCursor=0;initialPerceptionScan=true;
    auto core=GetModuleHandleW(L"Core.dll");
    perceptionExitRequested=reinterpret_cast<volatile int*>(GetProcAddress(core,"?GIsRequestingExit@@3HA"));
    if(!perceptionExitRequested||*perceptionExitRequested||!Alive(pawn))throw 1;
    perceptionPawnIndex=U(pawn,4);
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
        PerceptionRememberClass(object);
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
    if((perceptionExitRequested&&*perceptionExitRequested)||!Alive(pawn,perceptionPawnIndex)){
        InterlockedAnd(&activeMask,~static_cast<LONG>(InvisibleSam));return;
    }
    // Heat is used by turrets. Dogs use SmellRadius to locate Sam's scent points.
    Scale(pawn,perceptionFields[0].offset,0,InvisibleSam);
    const auto now=GetTickCount64();
    if(now<nextPerceptionScan)return;
    nextPerceptionScan=now+16;
    const DWORD count=objectArray[1];
    if(count>1000000)throw 1;
    if(perceptionCursor>=count)perceptionCursor=0;
    const DWORD remaining=count-perceptionCursor;
    const DWORD budget=initialPerceptionScan?count:(remaining<2048?remaining:2048);
    const DWORD end=perceptionCursor+budget;
    auto table=reinterpret_cast<Byte**>(objectArray[0]);
    for(DWORD i=perceptionCursor;i<end;++i){
        auto actor=table[i];
        if(Alive(actor))PerceptionRememberClass(actor);
        if(!PerceptionObserver(actor)||(U(actor,0x2c)&0x10)||U(actor,0x6c)!=U(player,0x6c))continue;
        PerceptionInstall(actor);
        if(PerceptionDog(actor))Scale(actor,perceptionFields[1].offset,0,InvisibleSam);
    }
    perceptionCursor=end==count?0:end;
    initialPerceptionScan=false;
}
