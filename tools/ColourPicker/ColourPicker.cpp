#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <string>
#include <sstream>
#include <iomanip>
#include <algorithm>
#include <vector>
#include <gdiplus.h>
#include <wincrypt.h>

static const UINT WM_TRAY = WM_APP + 1;
static const UINT ID_TRAY = 1001;
static const UINT ID_EXIT = 1002;
static const UINT ID_PICK = 1003;
static HWND g_hwnd=nullptr, g_tip=nullptr;
static HHOOK g_hook=nullptr;
static bool g_picking=false;
static COLORREF g_colour=RGB(0,0,0);
static POINT g_point{};
static HICON g_appIcon=nullptr;
static ULONG_PTR g_gdiplusToken=0;


static const char* SWIRL_PNG_B64 = "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";

HICON createSwirlIcon(){
    DWORD bytes=0;
    if(!CryptStringToBinaryA(SWIRL_PNG_B64,0,CRYPT_STRING_BASE64,nullptr,&bytes,nullptr,nullptr)) return LoadIcon(nullptr,IDI_APPLICATION);
    std::vector<BYTE> data(bytes);
    if(!CryptStringToBinaryA(SWIRL_PNG_B64,0,CRYPT_STRING_BASE64,data.data(),&bytes,nullptr,nullptr)) return LoadIcon(nullptr,IDI_APPLICATION);
    HGLOBAL mem=GlobalAlloc(GMEM_MOVEABLE,bytes);
    if(!mem) return LoadIcon(nullptr,IDI_APPLICATION);
    void* p=GlobalLock(mem); memcpy(p,data.data(),bytes); GlobalUnlock(mem);
    IStream* stream=nullptr;
    if(FAILED(CreateStreamOnHGlobal(mem,TRUE,&stream))) { GlobalFree(mem); return LoadIcon(nullptr,IDI_APPLICATION); }
    Gdiplus::Bitmap bitmap(stream);
    HICON icon=nullptr;
    if(bitmap.GetLastStatus()==Gdiplus::Ok) bitmap.GetHICON(&icon);
    stream->Release();
    return icon ? icon : LoadIcon(nullptr,IDI_APPLICATION);
}

std::wstring rgbText(COLORREF c){
    std::wstringstream s; s<<L"RGB("<<(int)GetRValue(c)<<L", "<<(int)GetGValue(c)<<L", "<<(int)GetBValue(c)<<L")"; return s.str();
}
std::wstring hexText(COLORREF c){
    std::wstringstream s; s<<L"#"<<std::uppercase<<std::hex<<std::setfill(L'0')
      <<std::setw(2)<<(int)GetRValue(c)<<std::setw(2)<<(int)GetGValue(c)<<std::setw(2)<<(int)GetBValue(c); return s.str();
}
COLORREF samplePixel(POINT p){
    HDC dc=GetDC(nullptr); COLORREF c=GetPixel(dc,p.x,p.y); ReleaseDC(nullptr,dc); return c;
}
bool copyText(const std::wstring& text){
    if(!OpenClipboard(g_hwnd)) return false;
    EmptyClipboard();
    SIZE_T bytes=(text.size()+1)*sizeof(wchar_t);
    HGLOBAL h=GlobalAlloc(GMEM_MOVEABLE,bytes);
    if(!h){CloseClipboard();return false;}
    memcpy(GlobalLock(h),text.c_str(),bytes); GlobalUnlock(h);
    SetClipboardData(CF_UNICODETEXT,h); CloseClipboard(); return true;
}
HCURSOR makeMagnifierCursor(){
    const int S=32; HDC sdc=GetDC(nullptr), dc=CreateCompatibleDC(sdc);
    HBITMAP colour=CreateCompatibleBitmap(sdc,S,S), old=(HBITMAP)SelectObject(dc,colour);
    RECT r{0,0,S,S}; FillRect(dc,&r,(HBRUSH)GetStockObject(WHITE_BRUSH));
    HPEN pen=CreatePen(PS_SOLID,3,RGB(0,0,0)), oldp=(HPEN)SelectObject(dc,pen);
    HBRUSH oldb=(HBRUSH)SelectObject(dc,GetStockObject(NULL_BRUSH));
    Ellipse(dc,4,4,21,21); MoveToEx(dc,18,18,nullptr); LineTo(dc,29,29);
    SelectObject(dc,oldb); SelectObject(dc,oldp); DeleteObject(pen); SelectObject(dc,old);
    HBITMAP mask=CreateBitmap(S,S,1,1,nullptr);
    ICONINFO ii{}; ii.fIcon=FALSE; ii.xHotspot=10; ii.yHotspot=10; ii.hbmMask=mask; ii.hbmColor=colour;
    HCURSOR cur=CreateIconIndirect(&ii); DeleteObject(mask); DeleteObject(colour); DeleteDC(dc); ReleaseDC(nullptr,sdc); return cur;
}
void stopPicking(){
    g_picking=false; if(g_hook){UnhookWindowsHookEx(g_hook);g_hook=nullptr;}
    ShowWindow(g_tip,SW_HIDE); SetCursor(LoadCursor(nullptr,IDC_ARROW));
}
LRESULT CALLBACK mouseHook(int code,WPARAM wp,LPARAM lp){
    if(code>=0 && g_picking && wp==WM_LBUTTONDOWN){
        auto* m=(MSLLHOOKSTRUCT*)lp; COLORREF c=samplePixel(m->pt);
        copyText(hexText(c)); MessageBeep(MB_OK); stopPicking(); return 1;
    }
    if(code>=0 && g_picking && wp==WM_RBUTTONDOWN){stopPicking(); return 1;}
    return CallNextHookEx(g_hook,code,wp,lp);
}
void startPicking(){
    if(g_picking) return; g_picking=true;
    g_hook=SetWindowsHookEx(WH_MOUSE_LL,mouseHook,GetModuleHandle(nullptr),0);
    SetTimer(g_hwnd,1,30,nullptr);
    SetCursor(makeMagnifierCursor());
}
void paintTip(HWND h){
    PAINTSTRUCT ps; HDC dc=BeginPaint(h,&ps); RECT rc; GetClientRect(h,&rc);
    HBRUSH bg=CreateSolidBrush(RGB(250,250,250)); FillRect(dc,&rc,bg); DeleteObject(bg);
    RECT sw{8,8,52,52}; HBRUSH b=CreateSolidBrush(g_colour); FillRect(dc,&sw,b); DeleteObject(b);
    FrameRect(dc,&sw,(HBRUSH)GetStockObject(BLACK_BRUSH));
    SetBkMode(dc,TRANSPARENT); SetTextColor(dc,RGB(20,20,20));
    std::wstring a=rgbText(g_colour), x=hexText(g_colour);
    TextOut(dc,62,10,a.c_str(),(int)a.size()); TextOut(dc,62,32,x.c_str(),(int)x.size());
    EndPaint(h,&ps);
}
LRESULT CALLBACK TipProc(HWND h,UINT m,WPARAM w,LPARAM l){
    if(m==WM_PAINT){paintTip(h);return 0;} return DefWindowProc(h,m,w,l);
}
void addTray(HWND h){
    NOTIFYICONDATA n{}; n.cbSize=sizeof(n);n.hWnd=h;n.uID=ID_TRAY;n.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;
    n.uCallbackMessage=WM_TRAY;n.hIcon=g_appIcon ? g_appIcon : LoadIcon(nullptr,IDI_APPLICATION);
    wcscpy_s(n.szTip,L"ColourPicker — click to pick a colour"); Shell_NotifyIcon(NIM_ADD,&n);
}
void delTray(HWND h){NOTIFYICONDATA n{};n.cbSize=sizeof(n);n.hWnd=h;n.uID=ID_TRAY;Shell_NotifyIcon(NIM_DELETE,&n);}
LRESULT CALLBACK WndProc(HWND h,UINT m,WPARAM w,LPARAM l){
    switch(m){
    case WM_CREATE:addTray(h);return 0;
    case WM_TRAY:
        if(l==WM_LBUTTONUP) startPicking();
        else if(l==WM_RBUTTONUP){POINT p;GetCursorPos(&p);HMENU menu=CreatePopupMenu();AppendMenu(menu,MF_STRING,ID_PICK,L"Pick colour");AppendMenu(menu,MF_SEPARATOR,0,nullptr);AppendMenu(menu,MF_STRING,ID_EXIT,L"Exit");SetForegroundWindow(h);TrackPopupMenu(menu,TPM_RIGHTBUTTON,p.x,p.y,0,h,nullptr);DestroyMenu(menu);} return 0;
    case WM_COMMAND: if(LOWORD(w)==ID_PICK)startPicking(); else if(LOWORD(w)==ID_EXIT)DestroyWindow(h); return 0;
    case WM_TIMER:
        if(g_picking){GetCursorPos(&g_point);g_colour=samplePixel(g_point);int x=g_point.x+20,y=g_point.y+20;SetWindowPos(g_tip,HWND_TOPMOST,x,y,225,62,SWP_NOACTIVATE|SWP_SHOWWINDOW);InvalidateRect(g_tip,nullptr,TRUE);SetCursor(makeMagnifierCursor());} return 0;
    case WM_DESTROY: stopPicking();delTray(h);PostQuitMessage(0);return 0;
    } return DefWindowProc(h,m,w,l);
}
int runTests(){
    if(rgbText(RGB(1,2,3))!=L"RGB(1, 2, 3)") return 1;
    if(hexText(RGB(0x0A,0xB0,0xFF))!=L"#0AB0FF") return 2;
    if(rgbText(RGB(255,0,128))!=L"RGB(255, 0, 128)") return 3;
    if(hexText(RGB(255,0,128))!=L"#FF0080") return 4;
    return 0;
}
int WINAPI wWinMain(HINSTANCE hi,HINSTANCE,LPWSTR cmd,int){
    if(cmd && wcsstr(cmd,L"/test")) return runTests();
    SetProcessDPIAware();
    Gdiplus::GdiplusStartupInput gdiplusInput;
    Gdiplus::GdiplusStartup(&g_gdiplusToken,&gdiplusInput,nullptr);
    g_appIcon=createSwirlIcon();
    WNDCLASS wc{};wc.lpfnWndProc=WndProc;wc.hInstance=hi;wc.hIcon=g_appIcon;wc.lpszClassName=L"ColourPickerHidden";RegisterClass(&wc);
    WNDCLASS tc{};tc.lpfnWndProc=TipProc;tc.hInstance=hi;tc.hCursor=LoadCursor(nullptr,IDC_ARROW);tc.lpszClassName=L"ColourPickerTip";RegisterClass(&tc);
    g_hwnd=CreateWindow(wc.lpszClassName,L"ColourPicker",WS_OVERLAPPED,0,0,0,0,nullptr,nullptr,hi,nullptr);
    g_tip=CreateWindowEx(WS_EX_TOOLWINDOW|WS_EX_TOPMOST|WS_EX_NOACTIVATE,tc.lpszClassName,L"",WS_POPUP|WS_BORDER,0,0,225,62,nullptr,nullptr,hi,nullptr);
    MSG msg;while(GetMessage(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessage(&msg);}if(g_appIcon)DestroyIcon(g_appIcon);Gdiplus::GdiplusShutdown(g_gdiplusToken);return (int)msg.wParam;
}