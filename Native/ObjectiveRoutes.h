#pragma once
#include <cwchar>
namespace ObjectiveRoutes {
struct Route {const wchar_t* map;const wchar_t* goal;const wchar_t* pattern;const wchar_t* label;};
// Explicit script entry points; never infer a label from an objective's display text.
// Source: EnhancedSC/UnrealScript/EchelonPattern/Classes (Kalinatek rooftop validated live; Tbilisi pending live tests).
inline const Route* Find(const wchar_t* map,const wchar_t* goal){
 static const Route routes[]={
 {L"1_1_1Tbilisi",L"BLACKBOX",L"P_1_1_1_Tbilisi_LambertComms",L"BoxFound"},
 {L"1_1_1Tbilisi",L"DEADDROP",L"P_1_1_1_Tbilisi_LambertComms",L"DeadDrop"},
 {L"1_1_1Tbilisi",L"STREETS",L"P_1_1_1_Tbilisi_LambertComms",L"SafeStreets"},
 {L"1_1_2Tbilisi",L"PRECINCT",L"P_1_1_2_Tbilisi_LambertComms",L"OpenDoor"},
 {L"1_1_2Tbilisi",L"BLAUSTEIN",L"P_1_1_2_Tbilisi_LambertComms",L"AtStation"},
 {L"1_1_2Tbilisi",L"ACCESS",L"P_1_1_2_Tbilisi_LambertComms",L"InSecurityRoom"},
 {L"1_1_2Tbilisi",L"EXTRACT",L"P_1_1_2_Tbilisi_LambertComms",L"MissionComplete"},
 {L"2_2_3_Kalinatek",L"ClearTheRoof",L"P_2_2_3_Ktech_OutroCinematic",L"MissionEnds"}
 };
 for(const auto& route:routes)if(!_wcsicmp(route.map,map)&&!_wcsicmp(route.goal,goal))return &route;
 return nullptr;
}
}
