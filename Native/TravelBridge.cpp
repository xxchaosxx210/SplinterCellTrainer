#include <windows.h>
#include <cstdio>
#include <cwchar>
#include "NpcProtocol.h"

struct Request { DWORD version; DWORD pid; HWND window; LONG result; wchar_t map[128]; };
static constexpr DWORD Version = 22;
static const wchar_t* MessageName = L"SCTrainer.DirectTravel.v22";
static void MappingName(wchar_t* out, DWORD pid) { swprintf_s(out, 80, L"Local\\SCTrainer.Travel.v22.%lu", pid); }

#ifdef BRIDGE_DLL
#include "Cheats.h"
#include "Spawn.h"
#include "Cameras.h"
#include "Fighting.h"
#ifdef SC_TRAVEL_DIAGNOSTICS
static wchar_t faultPath[MAX_PATH];
static LONG faultCount = 0;
static LONG faultSetup = 0;
static LONG CALLBACK CaptureFault(EXCEPTION_POINTERS* exception) {
    if (exception->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION || InterlockedIncrement(&faultCount) > 24) return EXCEPTION_CONTINUE_SEARCH;
    HANDLE file = CreateFileW(faultPath, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return EXCEPTION_CONTINUE_SEARCH;
    char text[2048]; auto c = exception->ContextRecord;
    MEMORY_BASIC_INFORMATION region = {};
    VirtualQuery(reinterpret_cast<void*>(c->Eip), &region, sizeof(region));
    wchar_t module[MAX_PATH] = L"<unmapped>";
    if(region.AllocationBase) GetModuleFileNameW(static_cast<HMODULE>(region.AllocationBase),module,MAX_PATH);
    int len = sprintf_s(text, "AV EIP=%08lX MODULE=%ls RVA=%08lX ESP=%08lX EAX=%08lX EBX=%08lX ECX=%08lX EDX=%08lX ESI=%08lX EDI=%08lX VA=%08lX\r\n",
        c->Eip,module,c->Eip-reinterpret_cast<DWORD>(region.AllocationBase),c->Esp,c->Eax,c->Ebx,c->Ecx,c->Edx,c->Esi,c->Edi,static_cast<DWORD>(exception->ExceptionRecord->ExceptionInformation[1]));
    DWORD written; WriteFile(file,text,len,&written,nullptr);
    __try {
        for(int i=0;i<64;++i) { len=sprintf_s(text,"%08lX ",reinterpret_cast<DWORD*>(c->Esp)[i]); WriteFile(file,text,len,&written,nullptr); }
        WriteFile(file,"\r\n",2,&written,nullptr);
    } __except(EXCEPTION_EXECUTE_HANDLER) {}
    CloseHandle(file); return EXCEPTION_CONTINUE_SEARCH;
}
static void EnableFaultCapture(HMODULE self) {
    if (InterlockedCompareExchange(&faultSetup,1,0)) return;
    GetModuleFileNameW(self,faultPath,MAX_PATH);
    auto slash=wcsrchr(faultPath,L'\\');
    if(slash) wcscpy_s(slash+1, MAX_PATH-(slash+1-faultPath),L"SCTravelFault.txt");
    AddVectoredExceptionHandler(1,CaptureFault);
}
#endif

static void Handle(Request* r) {
    // UnhookWindowsHookEx does not guarantee every concurrent callback has returned.
    // Pin callback code until process exit rather than allowing it to unload after a request.
    HMODULE self = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&Handle), &self)) { r->result = 9; return; }
#ifdef SC_TRAVEL_DIAGNOSTICS
    EnableFaultCapture(self);
#endif
    if (r->version != Version || r->pid != GetCurrentProcessId()) { r->result = 2; return; }
    HMODULE core = GetModuleHandleW(L"Core.dll"), engine = GetModuleHandleW(L"Engine.dll"), win = GetModuleHandleW(L"WinDrv.dll");
    auto objects = reinterpret_cast<DWORD*>(GetProcAddress(core, "?GObjObjects@UObject@@0V?$TArray@PAVUObject@@$0A@@@A"));
    auto engineVT = GetProcAddress(engine, "??_7UGameEngine@@6BUObject@@@");
    auto viewportVT = GetProcAddress(win, "??_7UWindowsViewport@@6BUObject@@@");
    auto getWindow = reinterpret_cast<void* (__thiscall*)(void*)>(GetProcAddress(win, "?GetWindow@UWindowsViewport@@UAEPAXXZ"));
    auto travel = reinterpret_cast<void (__thiscall*)(void*, void*, const wchar_t*, int, int, const wchar_t*)>(
        GetProcAddress(engine, "?SetClientTravel@UGameEngine@@UAEXPAVUPlayer@@PBGHW4ETravelType@@1@Z"));
    auto exec = reinterpret_cast<int (__thiscall*)(void*, const wchar_t*, void*)>(GetProcAddress(engine, "?Exec@UGameEngine@@UAEHPBGAAVFOutputDevice@@@Z"));
    auto log = reinterpret_cast<void**>(GetProcAddress(core, "?GLog@@3PAVFOutputDevice@@A"));
    if (!objects || !engineVT || !viewportVT || !getWindow || !travel || !exec || !log) { r->result = 3; return; }
    void* gameEngine = nullptr;
    void* viewport = nullptr;
    __try {
        auto table = reinterpret_cast<void**>(objects[0]);
        DWORD count = objects[1], capacity = objects[2];
        if (!table || count > capacity || count > 1000000) { r->result = 4; return; }
        for (DWORD i = 0; i < count; ++i) {
            void* object = table[i];
            if (!object) continue;
            auto vt = *reinterpret_cast<void**>(object);
            if (vt == engineVT) gameEngine = object;
            if (vt == viewportVT && getWindow(object) == r->window) viewport = object;
        }
        if (!gameEngine || !viewport) { r->result = 5; return; }
        auto playerExec = static_cast<BYTE*>(viewport) + 0x30;
        auto viewportExec = reinterpret_cast<int (__thiscall*)(void*, const wchar_t*, void*)>((*reinterpret_cast<void***>(playerExec))[0]);
        if (!wcscmp(r->map, L"--probe")) { r->result = 10; return; }
        if (!wcscmp(r->map, L"--state")) {
            auto getName = reinterpret_cast<const wchar_t* (__thiscall*)(void*)>(GetProcAddress(core, "?GetName@UObject@@QBEPBGXZ"));
            if (!getName) { r->result = 3; return; }
            auto player = *reinterpret_cast<BYTE**>(static_cast<BYTE*>(viewport) + 0x34);
            if (!player) { r->result = 12; return; }
            auto level = *reinterpret_cast<BYTE**>(player + 0x6C);
            if (!level) { r->result = 12; return; }
            auto package = *reinterpret_cast<void**>(level + 0x18);
            if (!package) { r->result = 12; return; }
            wcsncpy_s(r->map, getName(package), _TRUNCATE);
            r->result = 11;
            return;
        }
        if (TrainerCheats::SafeRequest(r, viewport, core, objects)) return;
        if (TrainerSpawn::SafeRequest(r, viewport, core, engine, objects)) return;
        if (TrainerCameras::Handle(r, viewport, core, objects)) return;
        if (TrainerFighting::Handle(r, viewport, core, objects)) return;
        if (!wcscmp(r->map, L"--health-on") || !wcscmp(r->map, L"--health-off")) {
            bool enable = !wcscmp(r->map, L"--health-on");
            auto getName = reinterpret_cast<const wchar_t* (__thiscall*)(void*)>(GetProcAddress(core, "?GetName@UObject@@QBEPBGXZ"));
            if (!getName) { r->result = 3; return; }
            auto player = *reinterpret_cast<BYTE**>(static_cast<BYTE*>(viewport) + 0x34);
            if (!player) { r->result = 16; return; }
            auto cls = *reinterpret_cast<BYTE**>(player + 0x24);
            bool isPlayer = false;
            for (int depth = 0; cls && depth < 24; ++depth) {
                if (!wcscmp(getName(cls), L"EPlayerController")) isPlayer = true;
                cls = *reinterpret_cast<BYTE**>(cls + 0x2C);
            }
            auto level = *reinterpret_cast<BYTE**>(player + 0x6C);
            auto package = level ? *reinterpret_cast<void**>(level + 0x18) : nullptr;
            if (!isPlayer || !package || !_wcsicmp(getName(package), L"menu")) { r->result = 16; return; }
            DWORD offset = 0, mask = 0;
            for (DWORD i = 0; i < count; ++i) {
                auto object = static_cast<BYTE*>(table[i]);
                if (!object || wcscmp(getName(object), L"bInvincible")) continue;
                auto owner = *reinterpret_cast<void**>(object + 0x18);
                auto type = *reinterpret_cast<void**>(object + 0x24);
                if (!owner || !type || wcscmp(getName(owner), L"EPlayerController") || wcscmp(getName(type), L"BoolProperty")) continue;
                offset = *reinterpret_cast<DWORD*>(object + 0x44);
                mask = *reinterpret_cast<DWORD*>(object + 0x54);
                break;
            }
            if (offset < 0x28 || offset > 0x4000 || offset % 4 || !mask || (mask & (mask - 1))) { r->result = 3; return; }
            auto flags = reinterpret_cast<volatile LONG*>(player + offset);
            if (enable) InterlockedOr(flags, static_cast<LONG>(mask));
            else InterlockedAnd(flags, static_cast<LONG>(~mask));
            if (((*flags & mask) != 0) != enable) { r->result = 7; return; }
            swprintf_s(r->map,L"%08lX:%08lX:%08lX:%08lX:%08lX",
                reinterpret_cast<DWORD>(viewport),reinterpret_cast<DWORD>(player),*reinterpret_cast<DWORD*>(player + 4),offset,mask);
            r->result = 15;
            return;
        }

        // Active-mission travel reproducibly crashes this installation even via OPEN.
        // Permit fresh launches from the menu only until mission teardown is understood.
        auto getMapName = reinterpret_cast<const wchar_t* (__thiscall*)(void*)>(GetProcAddress(core, "?GetName@UObject@@QBEPBGXZ"));
        auto currentPlayer = *reinterpret_cast<BYTE**>(static_cast<BYTE*>(viewport) + 0x34);
        if (!getMapName || !currentPlayer) { r->result = 13; return; }
        auto currentLevel = *reinterpret_cast<BYTE**>(currentPlayer + 0x6C);
        if (!currentLevel) { r->result = 13; return; }
        auto currentPackage = *reinterpret_cast<void**>(currentLevel + 0x18);
        if (!currentPackage || _wcsicmp(getMapName(currentPackage), L"menu")) { r->result = 13; return; }
        if (!r->map[0] || !wmemchr(r->map, L'\0', 128)) { r->result = 6; return; }
        for (const wchar_t* c = r->map; *c; ++c)
            if (!((*c >= L'0' && *c <= L'9') || (*c >= L'a' && *c <= L'z') ||
                  (*c >= L'A' && *c <= L'Z') || *c == L'_')) { r->result = 6; return; }
        // Use the same viewport FExec route as PlayerController.ConsoleCommand.
        if (GetForegroundWindow() != r->window || IsIconic(r->window)) { r->result = 14; return; }
        wchar_t command[160];
        swprintf_s(command, L"OPEN %s", r->map);
        // The console UI stays closed; only the internal menu command handler is used.
        if (!*log || !viewportExec(playerExec, command, *log)) { r->result = 8; return; }
        r->result = 1;
    } __except(EXCEPTION_EXECUTE_HANDLER) { r->result = 7; }
}
extern "C" __declspec(dllexport) LRESULT CALLBACK TravelHook(int code, WPARAM wp, LPARAM lp) {
    if (code >= 0) {
        auto msg = reinterpret_cast<CWPSTRUCT*>(lp);
        if (msg->message == RegisterWindowMessageW(MessageName)) {
            wchar_t name[80]; MappingName(name, GetCurrentProcessId());
            HANDLE mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
            if (mapping) {
                auto r = static_cast<Request*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Request)));
                if (r) { if (r->result == 0) Handle(r); UnmapViewOfFile(r); }
                CloseHandle(mapping);
            }
        }
    }
    return CallNextHookEx(nullptr, code, wp, lp);
}
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
#else
struct Search { DWORD pid; HWND window; };
static BOOL CALLBACK FindWindow(HWND window, LPARAM context) {
    auto s = reinterpret_cast<Search*>(context); DWORD pid;
    GetWindowThreadProcessId(window, &pid);
    if (pid == s->pid && IsWindowVisible(window) && !GetWindow(window, GW_OWNER)) {
        wchar_t cls[128]; GetClassNameW(window, cls, 128);
        // Only target the Unreal viewport, never a crash dialog.
        if (wcsstr(cls, L"Unreal")) { s->window = window; return FALSE; }
    }
    return TRUE;
}
int wmain(int argc, wchar_t** argv) {
    if (argc == 2 && !wcscmp(argv[1], L"--version")) { puts("SCTravelHost:0.22.2:protocol22"); return 0; }
    if (argc != 3) { puts("Expected process ID and map."); return 2; }
    Search search = { wcstoul(argv[1], nullptr, 10), nullptr };
    EnumWindows(FindWindow, reinterpret_cast<LPARAM>(&search));
    if (!search.window) { puts("Game viewport not found. Leave the game at its menu or in a mission."); return 3; }
    wchar_t path[MAX_PATH]; GetModuleFileNameW(nullptr, path, MAX_PATH);
    auto slash = wcsrchr(path, L'\\'); if (!slash) return 4;
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - path), L"SCTravelBridge.dll");
    auto dll = LoadLibraryW(path); if (!dll) { puts("Could not load travel bridge."); return 4; }
    auto proc = reinterpret_cast<HOOKPROC>(GetProcAddress(dll, "_TravelHook@12"));
    if (!proc) { puts("Bridge entry point missing."); FreeLibrary(dll); return 4; }
    wchar_t name[80]; MappingName(name, search.pid);
    auto mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(Request), name);
    if (!mapping || GetLastError() == ERROR_ALREADY_EXISTS) { puts("Another request is active."); return 4; }
    auto request = static_cast<Request*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Request)));
    if (!request) return 4;
    ZeroMemory(request, sizeof(Request)); request->version = Version; request->pid = search.pid; request->window = search.window;
    if (wcsncpy_s(request->map, argv[2], _TRUNCATE) != 0) return 4;
    HANDLE npcMapping=nullptr;
    NpcSnapshot* npcData=nullptr;
    if(!wcscmp(argv[2],L"--npc-list")) {
        wchar_t key[96];NpcSnapshotName(key,search.pid);
        npcMapping=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(NpcSnapshot),key);
        if(!npcMapping||GetLastError()==ERROR_ALREADY_EXISTS){puts("NPC snapshot busy.");return 4;}
        npcData=static_cast<NpcSnapshot*>(MapViewOfFile(npcMapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(NpcSnapshot)));
        if(!npcData){CloseHandle(npcMapping);return 4;}
    }
    auto thread = GetWindowThreadProcessId(search.window, nullptr);
    auto hook = SetWindowsHookExW(WH_CALLWNDPROC, proc, dll, thread);
    if (!hook) { printf("Could not connect to game thread (Windows error %lu).\n", GetLastError()); return 5; }
    DWORD_PTR response = 0;
    auto sent = SendMessageTimeoutW(search.window, RegisterWindowMessageW(MessageName), 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, &response);
    UnhookWindowsHookEx(hook);
    LONG result = request->result;
    wchar_t requestMap[128]; wcsncpy_s(requestMap, request->map, _TRUNCATE);
    UnmapViewOfFile(request); CloseHandle(mapping); FreeLibrary(dll);
    if (!sent) { puts("Game did not respond; map load not confirmed. Do not retry until responsive."); return 6; }
    if(npcData) {
        bool valid=sent&&result==32&&npcData->version==3&&npcData->count<=256;
        if(valid) {
            puts("NPCS:3");
            for(DWORD i=0;i<npcData->count;++i) {
                auto& n=npcData->entries[i];
                wprintf(L"%lu|%lu|%lu|%lu|%lu|%d|%.2f|%.2f|%s|%s|%s|%s|%s|%lu|%.3f|%.3f|%.3f|%s|%lu\n",n.id,n.present,n.removing,n.controller,n.moveFlags,n.health,n.speed,n.distance,n.type,n.pawnState,n.aiState,n.weapon,n.target,n.spawned,n.x,n.y,n.z,n.actorName,n.protectedGlobal);
            }
        }
        UnmapViewOfFile(npcData);CloseHandle(npcMapping);
        if(valid)return 0;
    }
    switch (result) {
    case 1: puts("Map travel queued on the game thread."); return 0;
    case 11: wprintf(L"STATE:%s\n", requestMap); return 0;
    case 12: puts("STATE:loading"); return 0;
    case 48: wprintf(L"%s\n", requestMap); return 0;
    case 49: puts("Guard combat is unavailable for the loaded AI scripts. No attack orders were issued."); return 27;
    case 46: wprintf(L"%s\n", requestMap); return 0;
    case 47: puts("Camera deactivation is unavailable for the loaded scripts. No cameras were changed."); return 26;
    case 45: puts("Quiet lights out is unavailable for the loaded lighting properties. No lights were changed."); return 25;
    case 44: puts("Lock bypass is unavailable for the loaded game scripts. No locks were changed."); return 24;
    case 43: puts("Enable noclip while Sam is standing normally, outside interactions or menus."); return 23;
    case 39: puts("Map NPC removal is disabled. Only trainer-spawned NPCs can be removed."); return 20;
    case 40: puts("Position rejected by the game collision check. Choose an unobstructed destination."); return 21;
    case 41: wprintf(L"%s\n",requestMap); return 0;
    case 42: puts("NPC registry is full (256 entries). Change maps to release old actors."); return 22;
    case 37: puts("Activity editing requires a living, conscious NPC in a normal AI state with its original controller."); return 19;
    case 38: puts("NPC activity goal assigned. Movement and search target a point four metres in front of Sam; the game handles pathfinding and reactions."); return 0;
    case 35: puts("This condition transition is unavailable. Revive requires an unconscious NPC with its original AI; dead NPCs cannot be resurrected."); return 18;
    case 36: puts("NPC condition transition requested."); return 0;
    case 33: puts("Health editing is only available for living conscious NPCs; this does not revive an NPC."); return 17;
    case 34: puts("NPC health updated."); return 0;
    case 30: puts("AI-group cleanup did not complete; actor removal was not scheduled."); return 16;
    case 28: puts("Spawned guard removal scheduled."); return 0;
    case 29: wprintf(L"NPC:%s\n",requestMap); return 0;
    case 21: wprintf(L"SPAWN:%s\n",requestMap); return 0;
    case 22: puts("Spawned guard removed (or no longer present)."); return 0;
    case 23: puts("The limit of eight active trainer NPCs has been reached."); return 12;
    case 24: puts("No ordinary armed guard type is available in this map."); return 13;
    case 25: puts("No clear spawn position in front of Sam. Move into an open area."); return 14;
    case 26: puts("Guard AI setup failed; created actors were removed."); return 15;
    case 18: wprintf(L"CHEATS:%s\n", requestMap); return 0;
    case 17: puts("Cheat session ended. Enable options again in the current mission."); return 11;
    case 15: wprintf(L"HEALTH:%s\n", requestMap); return 0;
    case 16: puts("Load a mission before enabling trainer options."); return 10;
    case 14: puts("Load blocked: the game must be visible and focused before map travel."); return 9;
    case 13: puts("Map change blocked: return to the main menu using the game's own menu first. Active-mission travel is not supported safely yet."); return 8;
    case 10: puts("Engine and active viewport located successfully; no map change requested."); return 0;
    case 5: puts("Active engine or viewport not found; no map change requested."); break;
    default: printf("Bridge validation failed (%ld); map load not confirmed.\n", result); break;
    }
    return 7;
}
#endif
