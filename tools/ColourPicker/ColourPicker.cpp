#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <string>
#include <sstream>
#include <iomanip>
#include <algorithm>

static const UINT WM_TRAY = WM_APP + 1;
static const UINT ID_TRAY = 1001;
static const UINT ID_EXIT = 1002;
static const UINT ID_PICK = 1003;
static HWND g_hwnd=nullptr, g_tip=nullptr;
static HHOOK g_hook=nullptr;
static bool g_picking=false;
static COLORREF g_colour=RGB(0,0,0);
static POINT g_point{};

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
        copyText(rgbText(c)); MessageBeep(MB_OK); stopPicking(); return 1;
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
    n.uCallbackMessage=WM_TRAY;n.hIcon=LoadIcon(nullptr,IDI_APPLICATION);
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
    return 0;
}
int WINAPI wWinMain(HINSTANCE hi,HINSTANCE,LPWSTR cmd,int){
    if(cmd && wcsstr(cmd,L"/test")) return runTests();
    SetProcessDPIAware();
    WNDCLASS wc{};wc.lpfnWndProc=WndProc;wc.hInstance=hi;wc.lpszClassName=L"ColourPickerHidden";RegisterClass(&wc);
    WNDCLASS tc{};tc.lpfnWndProc=TipProc;tc.hInstance=hi;tc.hCursor=LoadCursor(nullptr,IDC_ARROW);tc.lpszClassName=L"ColourPickerTip";RegisterClass(&tc);
    g_hwnd=CreateWindow(wc.lpszClassName,L"ColourPicker",WS_OVERLAPPED,0,0,0,0,nullptr,nullptr,hi,nullptr);
    g_tip=CreateWindowEx(WS_EX_TOOLWINDOW|WS_EX_TOPMOST|WS_EX_NOACTIVATE,tc.lpszClassName,L"",WS_POPUP|WS_BORDER,0,0,225,62,nullptr,nullptr,hi,nullptr);
    MSG msg;while(GetMessage(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessage(&msg);}return (int)msg.wParam;
}