#pragma once
struct EquipmentEntry {DWORD id,owned;int quantity,maximum;};
struct EquipmentSnapshot {DWORD version,count;EquipmentEntry entries[17];};
static void EquipmentSnapshotName(wchar_t* out,DWORD pid){swprintf_s(out,96,L"Local\\SCTrainer.Equipment.v1.%lu",pid);}
