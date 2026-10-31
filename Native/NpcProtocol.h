#pragma once
struct NpcEntry {DWORD id,present,removing,controller,moveFlags;int health;float speed,distance;wchar_t type[64],pawnState[64],aiState[64],weapon[64],target[64];};
struct NpcSnapshot {DWORD version,count;NpcEntry entries[32];};
static void NpcSnapshotName(wchar_t* out,DWORD pid){swprintf_s(out,96,L"Local\\SCTrainer.NpcSnapshot.v1.%lu",pid);}
