#pragma once
struct NpcEntry {DWORD id,present,removing,controller,moveFlags,spawned,protectedGlobal;int health;float speed,distance,x,y,z;wchar_t type[64],pawnState[64],aiState[64],weapon[64],target[64],actorName[64];};
struct NpcSnapshot {DWORD version,count;NpcEntry entries[256];};
static void NpcSnapshotName(wchar_t* out,DWORD pid){swprintf_s(out,96,L"Local\\SCTrainer.NpcSnapshot.v3.%lu",pid);}
