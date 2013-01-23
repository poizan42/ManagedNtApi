using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ManagedNtApi.Unsafe
{
  public class UnsafeNtFunctions
  {
    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static unsafe extern TEB* NtCurrentTeb();
  }
}
