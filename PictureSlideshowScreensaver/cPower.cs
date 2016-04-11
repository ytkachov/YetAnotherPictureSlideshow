/*==============================================================================================================
  
  [ cPower - Windows Power and Battery, Information / Functions Stub ]
  --------------------------------------------------------------------
  Copyright (c)2006-2007 aejw.com
  http://www.aejw.com/
  
 Build:         0004 - Jan 2007
 Requirments:   Vista, XP, 2000
 
 EULA:          Creative Commons - Attribution-ShareAlike 3.0
                http://creativecommons.org/licenses/by-sa/3.0/

==============================================================================================================*/
using System;
using System.Collections.Generic;
using System.Text;

namespace BatteryMonitor
{

  class cPower
  {

    /// <summary>
    /// Setting for the polling interval (in seconds) of battery infomation
    /// </summary>
    public int BatteryUpdateEvery
    {
      set
      {

        // set update value
        if (value > 0 && value < 43200)
          _pollPowerStatusEverySec = value;
        else
          _pollPowerStatusEverySec = 5;
      }
      get
      {

        // return update value
        return _pollPowerStatusEverySec;

      }
    }

    /// <summary>
    /// Percenage (0-100) of battery life remaining
    /// </summary>
    public int BatteryLifePercent
    {
      get
      {

        // return BatteryLifePercent from struct
        pollPowerStatus();
        int ret = _stPowerStatus.BatteryLifePercent;
        if (ret == 255)
          ret = 0;    // if 255 (error), state as 0
        return ret;

      }
    }

    /// <summary>
    /// Time in seconds of battery life remaining
    /// </summary>
    public int BatteryRemainingSeconds
    {
      get
      {
        // return BatteryLifeTime from struct
        pollPowerStatus();
        int ret = _stPowerStatus.BatteryLifeTime;
        if (ret == -1)
          ret = 0;    // if -1 (error), state as 0               
        return ret;

      }
    }

    /// <summary>
    /// Total time in seconds of battery life
    /// </summary>
    public int BatteryTotalSeconds
    {
      get
      {

        // return BatteryFullLifeTime from struct
        pollPowerStatus();
        int ret = _stPowerStatus.BatteryFullLifeTime;
        if (ret == -1)
          ret = 0;    // if -1 (error), state as 0
        return ret;

      }
    }

    /// <summary>
    /// Boolean value, stating if the computer has a battery
    /// </summary>
    public bool HasBattery
    {
      get
      {

        // return if system has battery (BatteryFlag < 128)
        pollPowerStatus();
        return (_stPowerStatus.BatteryFlag < 128);

      }
    }

    /// <summary>
    /// 3 teir battery guage, high, low and critical
    /// </summary>
    public cPowerBatteryLevel BatteryLevel
    {
      get
      {

        // read BatteryFlag from struct and set level flag
        pollPowerStatus();
        cPowerBatteryLevel ret = cPowerBatteryLevel.BatteryHigh;
        if (_stPowerStatus.BatteryFlag == 4)
          ret = cPowerBatteryLevel.BatteryCritical;
        else if (_stPowerStatus.BatteryFlag == 2)
          ret = cPowerBatteryLevel.BatteryLow;
        return ret;

      }
    }

    /// <summary>
    /// Boolean value stating if battery is charging
    /// </summary>
    public bool BatteryCharging
    {
      get
      {

        // return if flag is charging
        pollPowerStatus();
        return (_stPowerStatus.BatteryFlag == 8);

      }
    }

    /// <summary>
    /// Boolean value stating if the computer is currently running off main power
    /// </summary>
    public bool UsingAC
    {
      get
      {

        // return if ac flag from struct
        pollPowerStatus();
        return (_stPowerStatus.ACLineStatus == 1);

      }
    }

    /// <summary>
    /// Checks is a HDD is currently sleeping (wound down)
    /// </summary>
    /// <param name="deviceID">Integer value for volume, eg. 0</param>
    /// <returns>Boolean value stating if the hdd in question is sleeping</returns>
    public bool DriveAlseep(int deviceID)
    {

      // call power state api and return if drive is asleep
      bool fOn = false, ret = true;
      System.IntPtr ioHandle = this.getDeviceHandle(deviceID);
      if (ioHandle != System.IntPtr.Zero && cPower.GetDevicePowerState(ioHandle, out fOn))
        ret = fOn;
      cPower.CloseHandle(ioHandle);
      return !ret;

    }

    /// <summary>
    /// Set the power requirements for the current application, 
    /// eg. Hold display and system from suspend
    /// </summary>
    /// <param name="threadReq">Flag stating suspend mode, or release</param>
    public void SetPowerReq(cPowerThreadRequirments threadReq)
    {

      // set application power requirments
      cPower.SetThreadExecutionState(threadReq);

    }

    /// <summary>
    /// Boolean flag is system allows hibernation
    /// </summary>
    public bool CanHibernate
    {
      get
      {

        // return IsPwrHibernateAllowed api
        return cPower.IsPwrHibernateAllowed();

      }
    }

    /// <summary>
    /// Boolean flag is system allows the shutdown operation
    /// </summary>
    public bool CanShutdown
    {
      get
      {

        // return IsPwrShutdownAllowed api
        return cPower.IsPwrShutdownAllowed();

      }
    }

    /// <summary>
    /// Boolean flag is system allows suspend
    /// </summary>
    public bool CanSuspend
    {
      get
      {

        // return IsPwrSuspendAllowed api
        return cPower.IsPwrSuspendAllowed();

      }
    }

    /// <summary>
    /// Lock the active workstation
    /// </summary>
    public void LockWorkstation()
    {

      // call lock workstation
      if (!checkEntryPoint("user32.dll", "LockWorkStation"))
        throw new System.PlatformNotSupportedException("'LockWorkStation' method missing from 'user32.dll'!");
      LockWorkStation();

    }

    /// <summary>
    /// Log off the active user
    /// </summary>
    public void LogOff()
    {

      // reroute to LogOff(bool)
      this.LogOff(false);

    }

    /// <summary>
    /// Log off the active user
    /// </summary>
    /// <param name="force">Inform system to force operation</param>
    public void LogOff(bool force)
    {

      // call log off
      callShutdown(ShutdownFlags.LogOff, force);

    }

    /// <summary>
    /// Restart system
    /// </summary>
    public void Restart()
    {

      // reroute to Restart(bool)
      this.Restart(false);

    }

    /// <summary>
    /// Restart system
    /// </summary>
    /// <param name="force">Inform system to force operation</param>
    public void Restart(bool force)
    {

      // call restart
      callShutdown(ShutdownFlags.Restart, force);

    }

    /// <summary>
    /// Shutdown system
    /// </summary>
    public void Shutdown()
    {

      // reroute to Shutdown(bool)
      this.Shutdown(false);

    }

    /// <summary>
    /// Shutdown system
    /// </summary>
    /// <param name="force">Inform system to force operation</param>
    public void Shutdown(bool force)
    {

      // call shutdown
      callShutdown(ShutdownFlags.Shutdown, force);

    }

    /// <summary>
    /// Suspend system
    /// </summary>
    public void Suspend()
    {

      // reroute to Suspend(bool)
      this.Suspend(false);

    }

    /// <summary>
    /// Suspend system
    /// </summary>
    /// <param name="force">Inform system to force operation</param>
    public void Suspend(bool force)
    {

      // call suspend
      if (!this.checkEntryPoint("powrprof.dll", "SetSuspendState"))
        throw new System.PlatformNotSupportedException("'SetSuspendState' method missing from 'powrprof.dll'!");
      cPower.SetSuspendState(0, (int)(force ? 1 : 0), 0);

    }

    /// <summary>
    /// Hibernate system
    /// </summary>
    public void Hibernate()
    {

      // reroute to Hibernate(bool)
      this.Hibernate(false);

    }

    /// <summary>
    /// Hibernate system
    /// </summary>
    /// <param name="force">Inform system to force operation</param>
    public void Hibernate(bool force)
    {

      // call hibernate
      if (!this.checkEntryPoint("powrprof.dll", "SetSuspendState"))
        throw new System.PlatformNotSupportedException("'SetSuspendState' method missing from 'powrprof.dll'!");
      cPower.SetSuspendState(1, (int)(force ? 1 : 0), 0);

    }

    #region private functions

    private System.IntPtr getDeviceHandle(int deviceID)
    {
      // drive to open, eg. \\\\.\\PhysicalDrive0
      System.IntPtr hDevice = cPower.CreateFile("\\\\.\\PhysicalDrive" + deviceID.ToString(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE, System.IntPtr.Zero, OPEN_EXISTING, 0, System.IntPtr.Zero);
      if (hDevice.ToInt32() == -1)
        return System.IntPtr.Zero;
      return hDevice;
    }

    private structSystemPowerStatus _stPowerStatus;

    private int _pollPowerStatusEverySec = 5;

    private System.DateTime _pollPowerStatusLastPoll = System.DateTime.MinValue;

    private void pollPowerStatus()
    {
      if (System.DateTime.Now.Subtract(_pollPowerStatusLastPoll).TotalSeconds < _pollPowerStatusEverySec)
        return;
      _stPowerStatus = new structSystemPowerStatus();
      cPower.GetSystemPowerStatus(ref _stPowerStatus);
    }


    private bool checkEntryPoint(string library, string method)
    {
      System.IntPtr libPtr = LoadLibrary(library);
      if (!libPtr.Equals(System.IntPtr.Zero))
      {
        if (!GetProcAddress(libPtr, method).Equals(System.IntPtr.Zero))
        {
          FreeLibrary(libPtr);
          return (true);
        }
        FreeLibrary(libPtr);
      }
      return (false);
    }

    private void callShutdown(ShutdownFlags shutdownFlag, bool force)
    {

      if (!checkEntryPoint("kernel32.dll", "GetCurrentProcess"))
        throw new System.PlatformNotSupportedException("'GetCurrentProcess' method missing from 'kernel32.dll'!");

      // if force append to flag
      if (force)
        shutdownFlag = shutdownFlag | ShutdownFlags.Force;

      // open current process tokens
      System.IntPtr hCurrentProcess = GetCurrentProcess();
      System.IntPtr hNull = System.IntPtr.Zero;

      // request privileges
      if (checkEntryPoint("advapi32.dll", "OpenProcessToken"))
      {

        // open process token
        if (!OpenProcessToken(hCurrentProcess, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref hNull))
        {
          // failed...
        }

        // lookup privilege
        LuidAtt tLuid;
        tLuid.Count = 1;
        tLuid.Luid = 0;
        tLuid.Attr = SE_PRIVILEGE_ENABLED;
        if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, ref tLuid.Luid))
        {
          // failed...
        }

        // adjust privileges and call shutdown
        if (AdjustTokenPrivileges(hNull, false, ref tLuid, 0, System.IntPtr.Zero, System.IntPtr.Zero))
        {
          // failed...
        }
      }
      else
      {
        // assume pre 2000, and call exit windows anyway
      }

      // call exitWindows api
      if (ExitWindowsEx((int)shutdownFlag, 0))
      {
        // failed...
      }

    }

    #endregion

    #region API functions / calls

    [System.FlagsAttribute]
    private enum EXECUTION_STATE : uint
    {
      ES_SYSTEM_REQUIRED = 0x00000001,
      ES_DISPLAY_REQUIRED = 0x00000002,
      ES_USER_PRESENT = 0x00000004,     // legacy flag should not be used
      ES_CONTINUOUS = 0x80000000,
    }

    [System.FlagsAttribute]
    private enum ShutdownFlags : uint
    {
      LogOff = 0x00000000,
      Shutdown = 0x00000001,
      Restart = 0x00000002,
      PowerOff = 0x00000008,
      Force = 0x00000004,
      ForceIfHung = 0x00000010
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    internal struct LuidAtt
    {
      public int Count;
      public long Luid;
      public int Attr;
    }


    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct structSystemPowerStatus
    {
      public byte ACLineStatus;
      public byte BatteryFlag;
      public byte BatteryLifePercent;
      public byte Reserved1;
      public int BatteryLifeTime;
      public int BatteryFullLifeTime;
    }

    private const int FILE_SHARE_READ = 0x00000001;
    private const int FILE_SHARE_WRITE = 0x00000002;
    private const int OPEN_EXISTING = 3;
    private const int SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int TOKEN_QUERY = 0x00000008;
    private const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", EntryPoint = "CreateFileW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private extern static System.IntPtr CreateFile(string filename, System.UInt32 desiredAccess, System.UInt32 shareMode, System.IntPtr attributes, System.UInt32 creationDisposition, System.UInt32 flagsAndAttributes, System.IntPtr templateFile);

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", EntryPoint = "CloseHandle", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private extern static int CloseHandle(System.IntPtr handle);

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", EntryPoint = "SetThreadExecutionState", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private extern static EXECUTION_STATE SetThreadExecutionState(cPowerThreadRequirments state);

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", EntryPoint = "GetSystemPowerStatus", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern int GetSystemPowerStatus(ref structSystemPowerStatus systemPowerStatus);

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", EntryPoint = "GetDevicePowerState", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private extern static bool GetDevicePowerState(System.IntPtr hDevice, out bool fOn);

    [System.Runtime.InteropServices.DllImport("PowrProf.dll", EntryPoint = "IsPwrHibernateAllowed", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern bool IsPwrHibernateAllowed();

    [System.Runtime.InteropServices.DllImport("PowrProf.dll", EntryPoint = "IsPwrShutdownAllowed", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern bool IsPwrShutdownAllowed();

    [System.Runtime.InteropServices.DllImport("PowrProf.dll", EntryPoint = "IsPwrSuspendAllowed", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern bool IsPwrSuspendAllowed();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern System.IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool OpenProcessToken(System.IntPtr ProcessHandle, int DesiredAccess, ref System.IntPtr TokenHandle);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string SystemName, string Name, ref long LuidHandle);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(System.IntPtr TokenHandle, bool DisableAllPrivileges, ref LuidAtt NewState, int BufferLength, System.IntPtr PreviousState, System.IntPtr ReturnLength);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool ExitWindowsEx(int Flags, int Reason);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern void LockWorkStation();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "LoadLibraryA", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private static extern System.IntPtr LoadLibrary(string lpLibFileName);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "FreeLibrary", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private static extern int FreeLibrary(System.IntPtr hLibModule);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private static extern System.IntPtr GetProcAddress(System.IntPtr hModule, string lpProcName);

    [System.Runtime.InteropServices.DllImport("powrprof.dll", EntryPoint = "SetSuspendState", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private static extern int SetSuspendState(int hibernate, int forceCritical, int disableWakeEvent);

    #endregion


  }

  [System.FlagsAttribute]
  public enum cPowerBatteryLevel : int
  {
    BatteryHigh = 1,
    BatteryLow = 2,
    BatteryCritical = 4,
    BatteryCharging = 8,
    NoSystemBattery = 128,
    Unknown = 255
  }

  [System.FlagsAttribute]
  public enum cPowerThreadRequirments : uint
  {
    ReleaseHold = 0x80000000,
    HoldSystem = (0x00000001 | ReleaseHold),
    HoldDisplay = (0x00000002 | ReleaseHold),
    HoldSystemAndDisplay = (HoldSystem | HoldDisplay | ReleaseHold),
  }

}


