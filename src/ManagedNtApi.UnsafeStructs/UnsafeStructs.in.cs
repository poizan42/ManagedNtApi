using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ManagedNtApi.Unsafe
{
  class FlattenAttribute : Attribute { }
  class AnonymousStructAttribute : Attribute { }
  class UnionAttribute : Attribute { }

  [Flatten]
  [StructLayout(LayoutKind.Sequential, Pack=4)]
  public unsafe struct UNICODE_STRING
  {
     public ushort Length;
     public ushort MaximumLength;
     public char* buffer;

     public override string ToString()
     {
       return Marshal.PtrToStringUni((IntPtr)buffer);
     }

     unsafe ushort* Test
     {
       get
       {
         fixed (UNICODE_STRING* s = &this)
         {
          return &s->Length;
         }
       }
     }
  }

  // Based on wine's winnt.h, winternl.h definitions
  [Flatten]
  public unsafe struct NT_TIB
  {
    //struct _EXCEPTION_REGISTRATION_RECORD *ExceptionList;
    public IntPtr ExceptionList;
    public IntPtr StackBase;
    public IntPtr StackLimit;
    public IntPtr SubSystemTib;

    [Union, AnonymousStruct]
    public struct FiberOrVersionUnion
    {
      IntPtr FiberData;
      int Version;
    }
    [AnonymousStruct]
    public FiberOrVersionUnion fv; 
    public IntPtr ArbitraryUserPointer;
    public NT_TIB* Self;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct CLIENT_ID
  {
    public IntPtr /*HANDLE*/ UniqueProcess;
    public IntPtr /*HANDLE*/ UniqueThread;
  }
  
  [StructLayout(LayoutKind.Sequential)]
  public unsafe struct LIST_ENTRY
  {
    public LIST_ENTRY* Flink;
    public LIST_ENTRY* Blink;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct ACTIVATION_CONTEXT_STACK
  {
    public IntPtr ActiveFrame;
    /*RTL_ACTIVATION_CONTEXT_STACK_FRAME *ActiveFrame;*/
    public LIST_ENTRY                          FrameListCache;
    public uint                                Flags;
    public uint                                NextCookieSequenceNumber;
    public uint                                StackId; //this isn't included in the WINE version
  }

  public unsafe struct GDI_TEB_BATCH
  {
    public uint              Offset;
    public IntPtr /*HANDLE*/ HDC;
    public fixed uint        Buffer[0x136];
  };

  // Based on wine's winnt.h, winternl.h definitions
  /***********************************************************************
  * TEB data structure
  */
  [Flatten]
  public unsafe struct TEB
  {                                                                        /* win32/win64 */
    public NT_TIB                       Tib;                               /* 000/0000 */
    public IntPtr                       EnvironmentPointer;                /* 01c/0038 */
    public CLIENT_ID                    ClientId;                          /* 020/0040 */
    public IntPtr                       ActiveRpcHandle;                   /* 028/0050 */
    public IntPtr                       ThreadLocalStoragePointer;         /* 02c/0058 */
    public IntPtr /*PPEB*/              Peb;                               /* 030/0060 */
    public uint                         LastErrorValue;                    /* 034/0068 */
    public uint                         CountOfOwnedCriticalSections;      /* 038/006c */
    public IntPtr                       CsrClientThread;                   /* 03c/0070 */
    public IntPtr                       Win32ThreadInfo;                   /* 040/0078 */
    public fixed uint                   Win32ClientInfo[31];               /* 044/0080 */
    public IntPtr                       WOW32Reserved;                     /* 0c0/0100 */
    public uint                         CurrentLocale;                     /* 0c4/0108 */
    public uint                         FpSoftwareStatusRegister;          /* 0c8/010c */
    public fixed IntPtr                 SystemReserved1[54];               /* 0cc/0110 */
    public int                          ExceptionCode;                     /* 1a4/02c0 */
    public ACTIVATION_CONTEXT_STACK     ActivationContextStack;            /* 1a8/02c8 */
    //The addition of StackId to ACTIVATION_CONTEXT_STACK removes 4 of the spare bytes
    public fixed byte                   SpareBytes1[20];                   /* 1c0/02ec */
    public fixed IntPtr                 SystemReserved2[10];           /* 1d4/0300 */
    public GDI_TEB_BATCH                GdiTebBatch;                       /* 1fc/0350 */
    public IntPtr /*HANDLE*/            gdiRgn;                            /* 6dc/0838 */
    public IntPtr /*HANDLE*/            gdiPen;                            /* 6e0/0840 */
    public IntPtr /*HANDLE*/            gdiBrush;                          /* 6e4/0848 */
    public CLIENT_ID                    RealClientId;                      /* 6e8/0850 */
    public IntPtr /*HANDLE*/            GdiCachedProcessHandle;            /* 6f0/0860 */
    public uint                         GdiClientPID;                      /* 6f4/0868 */
    public uint                         GdiClientTID;                      /* 6f8/086c */
    public IntPtr                       GdiThreadLocaleInfo;               /* 6fc/0870 */
    public fixed uint                   UserReserved[5];                   /* 700/0878 */
    /* (wine's winternl.h says "glDispachTable" - most likely a typo...) */
    public fixed IntPtr                 glDispatchTable[280];              /* 714/0890 */
    public fixed IntPtr                 glReserved1[26];                   /* b74/1150 */
    public IntPtr                       glReserved2;                       /* bdc/1220 */
    public IntPtr                       glSectionInfo;                     /* be0/1228 */
    public IntPtr                       glSection;                         /* be4/1230 */
    public IntPtr                       glTable;                           /* be8/1238 */
    public IntPtr                       glCurrentRC;                       /* bec/1240 */
    public IntPtr                       glContext;                         /* bf0/1248 */
    public uint                         LastStatusValue;                   /* bf4/1250 */
    public UNICODE_STRING               StaticUnicodeString;               /* bf8/1258 used by advapi32 */
    public fixed Char                   StaticUnicodeBuffer[261];          /* c00/1268 used by advapi32 */
    public IntPtr                       DeallocationStack;                 /* e0c/1478 */
    public fixed IntPtr                 TlsSlots[64];                      /* e10/1480 */
    public LIST_ENTRY                   TlsLinks;                          /* f10/1680 */
    public IntPtr                       Vdm;                               /* f18/1690 */
    public IntPtr                       ReservedForNtRpc;                  /* f1c/1698 */
    public fixed IntPtr                 DbgSsReserved[2];                  /* f20/16a0 */
    public uint                         HardErrorDisabled;                 /* f28/16b0 */
    public fixed IntPtr                 Instrumentation[16];               /* f2c/16b8 */
    public IntPtr                       WinSockData;                       /* f6c/1738 */
    public uint                         GdiBatchCount;                     /* f70/1740 */
    public uint                         Spare2;                            /* f74/1744 */
    public IntPtr                       Spare3;                            /* f78/1748 */
    public IntPtr                       Spare4;                            /* f7c/1750 */
    public IntPtr                       ReservedForOle;                    /* f80/1758 */
    public uint                         WaitingOnLoaderLock;               /* f84/1760 */
    public fixed IntPtr                 Reserved5[3];                      /* f88/1768 */
    public IntPtr*                      TlsExpansionSlots;                 /* f94/1780 */
    public uint                         ImpersonationLocale;               /* f98/1788 */
    public uint                         IsImpersonating;                   /* f9c/178c */
    public IntPtr                       NlsCache;                          /* fa0/1790 */
    public IntPtr                       ShimData;                          /* fa4/1798 */
    public uint                         HeapVirtualAffinity;               /* fa8/17a0 */
    public IntPtr                       CurrentTransactionHandle;          /* fac/17a8 */
    public IntPtr                       ActiveFrame;                       /* fb0/17b0 */
#if X64
    public fixed IntPtr                 unknown[2];                        /*     17b8 */
#endif
    public IntPtr*                      FlsSlots;                          /* fb4/17c8 */
  }

  public struct POINT
  {
    public int x, y;
  }
}
