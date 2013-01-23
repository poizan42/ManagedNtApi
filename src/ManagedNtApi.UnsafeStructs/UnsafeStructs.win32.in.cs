using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

/* Unsafe structs belonging to the win32 subsystem. Generally everything here is
 * completely undocumented. The ReactOS source is a good place to look, and we
 * will generally use the same names as ReactOS for things not in the symbol 
 * files. */
namespace ManagedNtApi.Unsafe
{
  //The Win32ThreadInfo / W32THREAD
  //http://blog.csdn.net/coffeemay/article/details/1238777
  //http://www.reactos.org/wiki/Techwiki:Win32k/THREADINFO
  [StructLayout(LayoutKind.Sequential)]
  [Flatten]
  public unsafe struct THREADINFO
  {
    private const int WH_MIN = -1;
    private const int WH_MAX = 14; //TODO: When is this valid from? NT 4.0 had 12, 3.5 had 11

    public const int CWINHOOKS = WH_MAX - WH_MIN + 1;
    //[AnonymousStruct]

    //***************************************** begin: USER specific fields
    public IntPtr /*PTL*/               ptl;                // Listhead for thread lock list
    public IntPtr /*PPROCESSINFO*/      ppi;                // process info struct for this thread
    public IntPtr /*PQ*/                pq;                 // keyboard and mouse input queue
    public IntPtr /*PKL*/               spklActive;         // active keyboard layout for this thread
    public IntPtr /*PCLIENTTHREADINFO*/ pcti;               // Info that must be visible from client
    public IntPtr /*PDESKTOP*/          rpdesk;
    public IntPtr /*PDESKTOPINFO*/      pDeskInfo;          // Desktop info visible to client
    public IntPtr /*PCLIENTINFO*/       pClientInfo;        // Client info stored in TEB

    public int                          TIF_flags;          // TIF_ flags go here.
    public UNICODE_STRING*              pstrAppName;        // Application module name.
    public SMS*                         psmsSent;           // Most recent SMS this thread has sent
    public SMS*                         psmsCurrent;        // Received SMS this thread is currently processing
    public SMS*                         psmsReceiveList;    // SMSs to be processed
    public int                          timeLast;           // Time, position, and ID of last message
    public UIntPtr                      idLast;

    public int                          cQuit;
    public int                          exitCode;
    public IntPtr /*HDESK*/             hdesk;              // Desktop handle
    public int                          cPaintsReady;
    public uint                         cTimersReady;

    public IntPtr /*PMENUSTATE*/        pMenuState;
    
    [StructLayout(LayoutKind.Explicit)]
    private struct TdbOrInSta
    {
      [FieldOffset(0)]
      public IntPtr /*PTDB*/            ptdb;               // Win16Task Schedule data for WOW thread
      [FieldOffset(0)]
      public IntPtr /*PWINDOWSTATION*/  pwinsta;            // Window station for SYSTEM thread
    };
    [AnonymousStruct]
    public TdbOrInSta ts;

    public IntPtr /*PSVR_INSTANCE_INFO*/psiiList;           // thread DDEML instance list
    public int                          dwExpWinVer;
    public int                          dwCompatFlags;      // The Win 3.1 Compat flags
    public int                          dwCompatFlags2;     // new DWORD to extend compat flags for NT5+ features

    public IntPtr /*PQ*/                pqAttach;           // calculation variabled used in
                                                            // zzzAttachThreadInput()
    public THREADINFO*                  ptiSibling;         // pointer to sibling thread info
    public IntPtr /*PMOVESIZEDATA*/     pmsd;
    public int                          fsHooks;            // WHF_ Flags for which hooks are installed
    public IntPtr /*PHOOK*/             sphkCurrent;        // Hook this thread is currently processing

    public IntPtr /*PSBTRACK*/          pSBTrack;

    public IntPtr /*HANDLE*/            hEventQueueClient;
    public IntPtr /*PKEVENT*/           pEventQueueServer;
    public LIST_ENTRY                   PtiLink;            // Link to other threads on desktop
    public int                          iCursorLevel;       // keep track of each thread's level
    public POINT                        ptLast;

    public IntPtr /*PWND*/              spwndDefaultIme;    // Default IME Window for this thread
    public IntPtr /*PIMC*/              spDefaultImc;       // Default input context for this thread
    public IntPtr /*HKL*/               hklPrev;            // Previous active keyboard layout
    public int                          cEnterCount;
    public MLIST                        mlPost;             // posted message list.
    public ushort                       fsChangeBitsRemoved;// Bits removed during PeekMessage
    public char                         wchInjected;        // character from last VK_PACKET
    public int                          fsReserveKeys;      // Keys that must be sent to the active
                                        // active console window.
    public IntPtr /*PKEVENT*/           *apEvent;            // Wait array for xxxPollAndWaitForSingleObject
    public int /*ACCESS_MASK*/          amdesk;             // Granted desktop access
    public uint                         cWindows;           // Number of windows owned by this thread
    public uint                         cVisWindows;        // Number of visible windows on this thread

    //CWINHOOKS is windows version dependent
    //public IntPtr /*PHOOK*/             aphkStart[CWINHOOKS];   // Hooks registered for this thread
    //public CLIENTTHREADINFO             cti;              // Use this when no desktop is available
  }

  //http://www.reactos.org/wiki/Techwiki:Win32k/QUEUE
  public unsafe struct MLIST
  {  
    IntPtr /*PQMSG*/ pqmsgRead;
    IntPtr /*PQMSG*/ pqmsgWriteLast;
    int              cMsgs;
  }

  //http://mista.nu/blog/2011/02/11/thread-desynchronization-issues-in-windows-message-handling/
  //http://www.reactos.org/wiki/Techwiki:Win32k/SMS
  [Flatten]
  public unsafe struct SMS
  {
                                   //Win2k
    public SMS*              psmsNext;          // 000
    public SMS*              psmsReceiveNext;   // 004
    public int               tSent;             // 008
    public THREADINFO*       ptiSender;         // 00c
    public THREADINFO*       ptiReceiver;       // 010
    IntPtr /*SENDASYNCPROC*/ lpResultCallBack;  // 014
    public int               dwData;            // 018
    public THREADINFO*       ptiCallBackSender; // 01c
    public int               lRet;              // 020
    public int               flags;             // 024
    public IntPtr            wParam;            // 028
    public IntPtr            lParam;            // 02c
    public uint              message;           // 030
    public IntPtr /*PWND*/   spwnd;             // 034
    public IntPtr            pvCapture;         // 038
  }
}
