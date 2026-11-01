#pragma once
#include "LightPolicy.h"
// Included inside TrainerCheats. No damage, trigger, switch, sound or AI-change functions are called.
static Field quietFields[]={
    {L"Actor",L"LightType",L"ByteProperty"},
    {L"Actor",L"LightBrightness",L"ByteProperty"},
    {L"Actor",L"AmbientGlow",L"ByteProperty"},
    {L"Actor",L"UsesSpotLightBeam",L"BoolProperty"},
    {L"Actor",L"bGlowDisplay",L"BoolProperty"},
    {L"Actor",L"RestoreInitialLightType",L"BoolProperty"},
    {L"Actor",L"bLightChanged",L"BoolProperty"},
    {L"Actor",L"bLightCachingValid",L"BoolProperty"},
    {L"PlayerController",L"LightmapTextureCache",L"IntProperty"},
    {L"Actor",L"Owner",L"ObjectProperty"}
};
struct QuietLightRef {Byte* object;DWORD index,objectName,level;DWORD original[6];};
static std::vector<QuietLightRef> quietLights;
static bool quietReady=false;
static ULONGLONG nextQuietScan=0,nextQuietTick=0;
static DWORD QuietRead(Byte* object,int field) {
    const auto& f=quietFields[field];
    return field<3?*reinterpret_cast<Byte*>(object+f.offset):((U(object,f.offset)&f.mask)?1:0);
}
static void QuietWrite(Byte* object,int field,DWORD value) {
    const auto& f=quietFields[field];
    if(field<3)*reinterpret_cast<Byte*>(object+f.offset)=static_cast<Byte>(value);
    else if(value)InterlockedOr(reinterpret_cast<volatile LONG*>(object+f.offset),f.mask);
    else InterlockedAnd(reinterpret_cast<volatile LONG*>(object+f.offset),~f.mask);
}
static bool QuietRefAlive(const QuietLightRef& ref) {
    return Alive(ref.object,ref.index)&&U(ref.object,0x20)==ref.objectName&&U(ref.object,0x6c)==ref.level&&!(U(ref.object,0x2c)&0x10);
}
static bool QuietDestroyed(Byte* object) {
    for(int depth=0;object&&depth<8;++depth){
        if(!IsA(object,L"Actor"))break;
        auto frame=reinterpret_cast<Byte*>(U(object,0x0c));auto state=frame?reinterpret_cast<Byte*>(U(frame,0x1c)):nullptr;
        if(Alive(state)&&(!wcscmp(name(state),L"s_Destructed")||!wcscmp(name(state),L"s_Destrocuted")))return true;
        object=reinterpret_cast<Byte*>(U(object,quietFields[9].offset));
    }
    return false;
}
static bool QuietCarried(Byte* object) {
    for(int depth=0;object&&depth<8;++depth){
        if(!IsA(object,L"Actor"))break;
        if(object==pawn||object==player||IsA(object,L"EInventoryItem")||IsA(object,L"EPlayerController"))return true;
        object=reinterpret_cast<Byte*>(U(object,quietFields[9].offset));
    }
    return false;
}
static void QuietDirty(Byte* object) {
    QuietWrite(object,6,1);QuietWrite(object,7,0);
}
static void QuietPlayerCache() {
    if(Alive(player,playerIndex))*reinterpret_cast<int*>(player+quietFields[8].offset)=1;
}
static void QuietClear(){quietLights.clear();nextQuietScan=0;nextQuietTick=0;}
static void QuietRestore() {
    bool changed=false;
    if(quietReady)for(const auto& ref:quietLights){
        if(!LightPolicy::ShouldRestore(QuietRefAlive(ref),QuietRefAlive(ref)&&QuietDestroyed(ref.object)))continue;
        bool lightChanged=false;
        for(int i=0;i<6;++i)if(LightPolicy::StillMuted(QuietRead(ref.object,i))&&ref.original[i]!=0){QuietWrite(ref.object,i,ref.original[i]);lightChanged=true;}
        if(lightChanged){QuietDirty(ref.object);changed=true;}
    }
    if(changed)QuietPlayerCache();
    QuietClear();
}
static void QuietResolve() {
    quietReady=false;
    for(auto& f:quietFields){f.offset=0;f.mask=0;f.count=0;}
    auto table=reinterpret_cast<Byte**>(objectArray[0]);auto count=objectArray[1];if(count>1000000)throw 1;
    for(DWORD i=0;i<count;++i){
        auto object=table[i];if(!object)continue;
        auto owner=reinterpret_cast<Byte*>(U(object,0x18)),type=reinterpret_cast<Byte*>(U(object,0x24));if(!owner||!type)continue;
        const auto n=name(object),on=name(owner),tn=name(type);
        for(auto& f:quietFields)if(!wcscmp(n,f.name)&&!wcscmp(on,f.owner)&&!wcscmp(tn,f.type)){
            f.offset=U(object,0x44);f.mask=U(object,0x54);f.count=*reinterpret_cast<WORD*>(object+0x38);
        }
    }
    for(const auto& f:quietFields){
        if(f.offset<0x28||f.offset>0x8000||f.count!=1)throw 1;
        if(!wcscmp(f.type,L"BoolProperty")&&(!f.mask||(f.mask&(f.mask-1))||f.offset%4))throw 1;
    }
    quietReady=true;nextQuietScan=0;nextQuietTick=0;
}
static void QuietTick() {
    if(!quietReady)return;
    auto now=GetTickCount64();if(now<nextQuietTick)return;nextQuietTick=now+100;
    bool changed=false;
    if(now>=nextQuietScan){
        auto table=reinterpret_cast<Byte**>(objectArray[0]);auto count=objectArray[1];if(count>1000000)throw 1;
        for(DWORD i=0;i<count;++i){
            auto object=table[i];if(!object)continue;
            bool supported=IsA(object,L"Light")||IsA(object,L"EGamePlayObjectLight");if(!supported)continue;
            if(!LightPolicy::ShouldSuppress(true,U(object,0x6c)==U(player,0x6c),QuietCarried(object),(U(object,0x2c)&0x10)!=0)||QuietDestroyed(object))continue;
            bool exists=false;for(const auto& ref:quietLights)if(ref.object==object&&ref.index==U(object,4)){exists=true;break;}
            if(exists||quietLights.size()>=4096)continue;
            QuietLightRef ref={object,U(object,4),U(object,0x20),U(object,0x6c),{}};
            for(int f=0;f<6;++f)ref.original[f]=QuietRead(object,f);
            quietLights.push_back(ref);
        }
        nextQuietScan=now+1000;
    }
    for(auto it=quietLights.begin();it!=quietLights.end();){
        if(!QuietRefAlive(*it)||QuietDestroyed(it->object)){it=quietLights.erase(it);continue;}
        bool lightChanged=false;
        for(int i=0;i<6;++i){
            DWORD value=QuietRead(it->object,i);
            if(value){it->original[i]=value;QuietWrite(it->object,i,0);lightChanged=true;}
        }
        if(lightChanged){QuietDirty(it->object);changed=true;}
        ++it;
    }
    if(changed)QuietPlayerCache();
}
