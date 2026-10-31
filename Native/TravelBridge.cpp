#include <windows.h>
#include <cstdio>
#include <cwchar>

struct Request { DWORD version; DWORD pid; HWND window; LONG result; wchar_t map[128]; };
static constexpr DWORD Version = 1;
static const wchar_t* MessageName = L"SCTrainer.DirectTravel.v1";
static void MappingName(wchar_t* out, DWORD pid) { swprintf_s(out, 80, L"Local\\SCTrainer.Travel.%lu", pid); }

#ifdef BRIDGE_DLL
static void Handle(Request* r) {
    if (r->version != Version || r->pid != GetCurrentProcessId()) { r->result = 2; return; }
    HMODULE core = GetModuleHandleW(L"Core.dll"), engine = GetModuleHandleW(L"Engine.dll"), win = GetModuleHandleW(L"WinDrv.dll");
    auto objects = reinterpret_cast<DWORD*>(GetProcAddress(core, "?GObjObjects@UObject@@0V?$TArray@PAVUObject@@$0A@@@A"));
    auto engineVT = GetProcAddress(engine, "??_7UGameEngine@@6BUObject@@@");
    auto viewportVT = GetProcAddress(win, "??_7UWindowsViewport@@6BUObject@@@");
    auto getWindow = reinterpret_cast<void* (__thiscall*)(void*)>(GetProcAddress(win, "?GetWindow@UWindowsViewport@@UAEPAXXZ"));
    auto travel = reinterpret_cast<void (__thiscall*)(void*, void*, const wchar_t*, int, int, const wchar_t*)>(
        GetProcAddress(engine, "?SetClientTravel@UGameEngine@@UAEXPAVUPlayer@@PBGHW4ETravelType@@1@Z"));
    if (!objects || !engineVT || !viewportVT || !getWindow || !travel) { r->result = 3; return; }
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
        if (!wcscmp(r->map, L"--probe")) { r->result = 10; return; }
        if (!r->map[0] || !wmemchr(r->map, L'\0', 128)) { r->result = 6; return; }
        for (const wchar_t* c = r->map; *c; ++c)
            if (!((*c >= L'0' && *c <= L'9') || (*c >= L'a' && *c <= L'z') ||
                  (*c >= L'A' && *c <= L'Z') || *c == L'_')) { r->result = 6; return; }
        // Absolute travel, reset items, empty additional URL. The engine queues the change for its tick.
        travel(gameEngine, viewport, r->map, 0, 0, L"");
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
    auto thread = GetWindowThreadProcessId(search.window, nullptr);
    auto hook = SetWindowsHookExW(WH_CALLWNDPROC, proc, dll, thread);
    if (!hook) { printf("Could not connect to game thread (Windows error %lu).\n", GetLastError()); return 5; }
    DWORD_PTR response = 0;
    auto sent = SendMessageTimeoutW(search.window, RegisterWindowMessageW(MessageName), 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, &response);
    UnhookWindowsHookEx(hook);
    LONG result = request->result;
    UnmapViewOfFile(request); CloseHandle(mapping); FreeLibrary(dll);
    if (!sent) { puts("Game did not respond; map load not confirmed. Do not retry until responsive."); return 6; }
    switch (result) {
    case 1: puts("Map travel queued on the game thread."); return 0;
    case 10: puts("Engine and active viewport located successfully; no map change requested."); return 0;
    case 5: puts("Active engine or viewport not found; no map change requested."); break;
    default: printf("Bridge validation failed (%ld); map load not confirmed.\n", result); break;
    }
    return 7;
}
#endif
