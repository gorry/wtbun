// Windows Terminal のプロセスに読み込まれ、wtbun が登録したウィンドウの「閉じる」要求を止めて wtbun に知らせる DLL。
// このコードが落ちると Windows Terminal の全ウィンドウが落ちるので、止める・知らせる・通すこと以外はしない。
// wtbun がいないときや応答しないときは、要求をそのまま通す。
#include <windows.h>
#include <commctrl.h>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "comctl32.lib")

namespace
{
    const UINT_PTR SubclassId = 0x57544255;
    // 登録済みのウィンドウが答える値。登録がなければ DefWindowProc が 0 を返す
    const LRESULT Answered = 0x57544255;
    const wchar_t AllowCloseProperty[] = L"wtbun.hook.allowClose";
    const DWORD ReleaseDelayMs = 1000;

    HMODULE g_self;
    UINT g_attach;
    UINT g_detach;
    UINT g_closeNow;
    UINT g_closeRequested;

    void EnsureMessages()
    {
        // 値はどのスレッドで求めても同じ。最後に g_attach を書くので、g_attach が見えれば全部そろっている
        if (g_attach == 0)
        {
            g_detach = RegisterWindowMessageW(L"wtbun.hook.detach");
            g_closeNow = RegisterWindowMessageW(L"wtbun.hook.closeNow");
            g_closeRequested = RegisterWindowMessageW(L"wtbun.hook.closeRequested");
            g_attach = RegisterWindowMessageW(L"wtbun.hook.attach");
        }
    }

    DWORD WINAPI ReleaseSelf(LPVOID)
    {
        // サブクラスの処理を抜けきるのを待ってから、参照を手放す
        Sleep(ReleaseDelayMs);
        FreeLibraryAndExitThread(g_self, 0);
    }

    LRESULT CALLBACK SubclassProc(HWND, UINT, WPARAM, LPARAM, UINT_PTR, DWORD_PTR);

    void Remove(HWND window)
    {
        if (RemoveWindowSubclass(window, SubclassProc, SubclassId))
        {
            // スレッドを作れなければ、参照を持ったままにする（アンロードされないだけで害はない）
            HANDLE thread = CreateThread(nullptr, 0, ReleaseSelf, nullptr, 0, nullptr);
            if (thread != nullptr)
            {
                CloseHandle(thread);
            }
        }
    }

    // wtbun に知らせられたら true。false なら要求をそのまま通す
    bool Hold(HWND window, HWND owner)
    {
        return IsWindow(owner) && !IsHungAppWindow(owner)
            && PostMessageW(owner, g_closeRequested, reinterpret_cast<WPARAM>(window), 0);
    }

    LRESULT CALLBACK SubclassProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR, DWORD_PTR refData)
    {
        HWND owner = reinterpret_cast<HWND>(refData);
        if (message == g_attach)
        {
            return Answered;
        }
        if (message == g_detach)
        {
            Remove(window);
            return Answered;
        }
        if (message == g_closeNow)
        {
            SetPropW(window, AllowCloseProperty, reinterpret_cast<HANDLE>(1));
            PostMessageW(window, WM_CLOSE, 0, 0);
            return Answered;
        }
        switch (message)
        {
        case WM_CLOSE:
            if (RemovePropW(window, AllowCloseProperty) == nullptr && Hold(window, owner))
            {
                return 0;
            }
            break;
        case WM_SYSCOMMAND:
            if ((wParam & 0xFFF0) == SC_CLOSE && Hold(window, owner))
            {
                return 0;
            }
            break;
        case WM_NCDESTROY:
            RemovePropW(window, AllowCloseProperty);
            Remove(window);
            break;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    void Attach(HWND window, HWND owner)
    {
        DWORD_PTR current;
        if (GetWindowSubclass(window, SubclassProc, SubclassId, &current))
        {
            // wtbun が起動し直したときは、知らせる先だけを変える
            SetWindowSubclass(window, SubclassProc, SubclassId, reinterpret_cast<DWORD_PTR>(owner));
            return;
        }
        // フックが外れても（wtbun が強制終了しても）サブクラスがある間はアンロードされないよう、参照を持つ
        HMODULE module;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, reinterpret_cast<LPCWSTR>(&SubclassProc), &module))
        {
            return;
        }
        if (!SetWindowSubclass(window, SubclassProc, SubclassId, reinterpret_cast<DWORD_PTR>(owner)))
        {
            FreeLibrary(module);
        }
    }
}

extern "C" __declspec(dllexport) LRESULT CALLBACK HookProc(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HC_ACTION)
    {
        EnsureMessages();
        const CWPSTRUCT* call = reinterpret_cast<const CWPSTRUCT*>(lParam);
        if (call->message == g_attach)
        {
            Attach(call->hwnd, reinterpret_cast<HWND>(call->wParam));
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}
