#pragma once
namespace TrainerAlarms {
using namespace TrainerCheats;

static bool Handle(Request* request,void* viewport,HMODULE core,DWORD* objects) {
    const bool start=!wcscmp(request->map,L"--alarm-start");
    if(!start&&wcscmp(request->map,L"--alarm-clear"))return false;
    request->result=72;
    objectArray=objects;
    name=reinterpret_cast<NameFn>(GetProcAddress(core,"?GetName@UObject@@QBEPBGXZ"));
    auto find=reinterpret_cast<TrainerSpawn::FindFn>(GetProcAddress(core,"?FindFunction@UObject@@QAEPAVUFunction@@VFName@@H@Z"));
    auto process=reinterpret_cast<TrainerSpawn::EventFn>(GetProcAddress(core,"?ProcessEvent@UObject@@UAEXPAVUFunction@@PAX1@Z"));
    if(!name||!find||!process)return true;

    auto player=reinterpret_cast<Byte*>(U(static_cast<Byte*>(viewport),0x34));
    if(!IsA(player,L"EPlayerController")){request->result=16;return true;}
    auto level=reinterpret_cast<Byte*>(U(player,0x6c));
    auto package=Alive(level)?reinterpret_cast<Byte*>(U(level,0x18)):nullptr;
    if(!Alive(package)||!_wcsicmp(name(package),L"menu")){request->result=16;return true;}
    if(GetForegroundWindow()!=request->window||IsIconic(request->window)){request->result=14;return true;}
    const auto pawnOffset=TrainerSpawn::Property(objects,name,L"EPlayerController",L"EPawn");
    const auto locationOffset=TrainerSpawn::Property(objects,name,L"Actor",L"Location");
    if(!pawnOffset||!locationOffset)return true;
    auto sam=reinterpret_cast<Byte*>(U(player,pawnOffset));
    if(!IsA(sam,L"EPawn")){request->result=16;return true;}

    struct AlarmCall {Byte* actor;void* function;};
    std::vector<AlarmCall> calls;
    float nearestDistance=INFINITY;
    const auto position=*reinterpret_cast<TrainerSpawn::Vec*>(sam+locationOffset);
    auto table=reinterpret_cast<Byte**>(objects[0]);
    for(DWORD i=0;i<objects[1];++i){
        auto actor=table[i];
        if(!IsA(actor,L"EAlarm")||U(actor,0x6c)!=reinterpret_cast<DWORD>(level)||(U(actor,0x2c)&0x10))continue;
        if(_wcsicmp(TrainerSpawn::State(actor,name),start?L"s_Off":L"s_On"))continue;
        auto function=TrainerSpawn::Function(actor,start?L"EnableAlarm":L"DisableAlarm",objects,name,find);
        if(!function||!TrainerSpawn::Param(function,L"Instigator",0,4,objects,name))return true;
        if(start){
            if(!TrainerSpawn::Param(function,L"Triggerer",4,4,objects,name)||
               !TrainerSpawn::Param(function,L"_bForceUpdatePos",8,4,objects,name))return true;
            const auto location=*reinterpret_cast<TrainerSpawn::Vec*>(actor+locationOffset);
            const float dx=location.x-position.x,dy=location.y-position.y,dz=location.z-position.z;
            const float distance=dx*dx+dy*dy+dz*dz;
            if(distance>=nearestDistance||!std::isfinite(distance))continue;
            nearestDistance=distance;
            calls.clear();
        }
        calls.push_back({actor,function});
    }
    unsigned changed=0;
    for(const auto& call:calls){
        if(!Alive(call.actor)||(U(call.actor,0x2c)&0x10))continue;
        struct {Byte* instigator;Byte* triggerer;DWORD force;} parameters={sam,nullptr,0};
        process(call.actor,call.function,&parameters,nullptr);
        if(Alive(call.actor)&&!_wcsicmp(TrainerSpawn::State(call.actor,name),start?L"s_On":L"s_Off"))++changed;
    }
    if(calls.empty())wcscpy_s(request->map,start?L"No inactive alarms found in this level.":L"No active alarms found in this level.");
    else swprintf_s(request->map,start?L"Alarms started: %u.":L"Active alarms cleared: %u.",changed);
    request->result=73;
    return true;
}
}
