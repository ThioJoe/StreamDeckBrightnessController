using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// Uses some logic from: https://github.com/dend/decksurf-sdk
// But this is a simplified version, focusing on brightness control, and using direct P/Invoke calls to SetupAPI and HID APIs instead of HIDSharp.

#nullable enable

namespace StreamDeckBrightnessControl;
internal static class StreamDeckHandler
{
    // Simple class to hold device info
    public class StreamDeckInfo(string devicePath, ushort vendorId, ushort productId)
    {
        public string DevicePath { get; set; } = devicePath;
        public ushort VendorId { get; set; } = vendorId;
        public ushort ProductId { get; set; } = productId;
    }

    // Contains P/Invoke definitions and methods for SetupAPI device discovery
    internal static class DeviceFinder
    {
        #region SetupAPI P/Invoke Definitions

        // From setupapi.h
        internal const int DIGCF_PRESENT = 0x00000002;
        internal const int DIGCF_DEVICEINTERFACE = 0x00000010;

        // From winerror.h
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved; // Changed from UIntPtr to IntPtr for broader CLS compliance if needed, often equivalent
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved; // Changed from UIntPtr to IntPtr
        }

        // Needs to be Unicode (W) version for C# strings
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SP_DEVICE_INTERFACE_DETAIL_DATA_W
        {
            public uint cbSize;
            public char DevicePath; // Just a single char placeholder
        }

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern void HidD_GetHidGuid(out Guid HidGuid);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            [MarshalAs(UnmanagedType.LPWStr)] string? Enumerator, // Use LPWStr for Unicode
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(
             IntPtr DeviceInfoSet,
             uint MemberIndex,
             ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool SetupDiGetDeviceInstanceId(
             IntPtr DeviceInfoSet,
             ref SP_DEVINFO_DATA DeviceInfoData,
             StringBuilder DeviceInstanceId,
             uint DeviceInstanceIdSize,
             out uint RequiredSize);


        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData, // Optional: SP_DEVINFO_DATA structure
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData, // Pass IntPtr.Zero to get size
            uint DeviceInterfaceDetailDataSize,
            out uint RequiredSize,
            IntPtr DeviceInfoData); // Optional: SP_DEVINFO_DATA

        // Overload for getting the detail data structure
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            ref SP_DEVICE_INTERFACE_DETAIL_DATA_W DeviceInterfaceDetailData, // Pass struct ref
            uint DeviceInterfaceDetailDataSize,
            out uint RequiredSize,
            IntPtr DeviceInfoData); // Optional: SP_DEVINFO_DATA


        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        #endregion

        // Find Stream Deck Devices
        public static List<StreamDeckInfo> FindStreamDecks()
        {
            var devices = new List<StreamDeckInfo>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr deviceInfoSet = SetupDiGetClassDevs(ClassGuid: ref hidGuid, Enumerator: null, hwndParent: IntPtr.Zero, Flags: DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == IntPtr.Zero) // Check for INVALID_HANDLE_VALUE is often -1 (IntPtr), Zero might also indicate failure contextually
            {
                Console.WriteLine($"Error getting device info set: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                return devices;
            }

            try
            {
                uint deviceIndex = 0;
                var deviceInfoData = new SP_DEVINFO_DATA();
                deviceInfoData.cbSize = (uint)Marshal.SizeOf(deviceInfoData);

                // Enumerate devices first to get instance IDs
                while (SetupDiEnumDeviceInfo(deviceInfoSet, deviceIndex, ref deviceInfoData))
                {
                    deviceIndex++;
                    StringBuilder instanceIdBuilder = new StringBuilder(256); // Max length for instance ID
                    if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, instanceIdBuilder, (uint)instanceIdBuilder.Capacity, out _))
                    {
                        string instanceId = instanceIdBuilder.ToString();
                        // Check if it contains Elgato VID
                        Match match = Regex.Match(instanceId, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                        if (match.Success && match.Groups[1].Value.Equals("0FD9", StringComparison.OrdinalIgnoreCase))
                        {
                            ushort vid = Convert.ToUInt16(match.Groups[1].Value, 16);
                            ushort pid = Convert.ToUInt16(match.Groups[2].Value, 16);

                            // Now enumerate interfaces for *this specific device*
                            var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                            interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);
                            uint interfaceIndex = 0;

                            // Use deviceInfoData.DevInst to filter interfaces for this device if needed, though often iterating all HID interfaces works
                            while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, interfaceIndex, ref interfaceData))
                            {
                                interfaceIndex++;
                                // Get required buffer size for detail data
                                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
                                if (requiredSize == 0) continue; // Skip if no detail

                                // Allocate buffer and get detail data struct
                                IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                                var detailData = new SP_DEVICE_INTERFACE_DETAIL_DATA_W();
                                if (IntPtr.Size == 8) // 64-bit
                                {
                                    detailData.cbSize = 8;
                                }
                                else // 32-bit
                                {
                                    // Common values are 5 or 6. Let's try 6 (DWORD + WCHAR)
                                    // If 6 fails, try 5. This value tells the API the format of the struct buffer.
                                    detailData.cbSize = 6; // Try 5 if 6 doesn't work on 32-bit .NET 4.8
                                }
                                // Copy the structure (specifically cbSize) to the buffer
                                Marshal.StructureToPtr(detailData, detailDataBuffer, false);


                                if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                                {
                                    // Get the device path string from the buffer, starting after cbSize field
                                    // Offset by the size of the fixed part of the struct (usually 4 bytes for cbSize)
                                    string? devicePath = Marshal.PtrToStringUni(IntPtr.Add(detailDataBuffer, (int)Marshal.OffsetOf<SP_DEVICE_INTERFACE_DETAIL_DATA_W>("DevicePath")));

                                    // Check if this device path belongs to the VID/PID we found earlier.
                                    // The path itself often contains the VID/PID string, providing another check.
                                    if (devicePath != null && devicePath.IndexOf($"VID_{vid:X4}&PID_{pid:X4}", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        // Avoid adding duplicates if multiple interfaces exist for the same hardware PID
                                        if (!devices.Exists(d => d.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            devices.Add(new StreamDeckInfo
                                            (
                                                devicePath: devicePath,
                                                vendorId: vid,
                                                productId: pid
                                            ));
                                            // Found the interface for this specific device instance, can break inner loop
                                            // break; // Commented out: Sometimes multiple interfaces are relevant, list all? For brightness one is enough. Let's break.
                                            goto NextDevice; // Jump to the next device enumeration
                                        }
                                    }
                                }
                                else
                                {
                                    // Optional: Log error getting detail data
                                    //Console.WriteLine($"Error getting device interface detail: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                                }

                                Marshal.FreeHGlobal(detailDataBuffer); // Clean up allocated memory
                            }
                        }
                    }
                NextDevice:; // Label for goto
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet); // Essential cleanup
            }

            return devices;
        }
    }

    // Contains P/Invoke definitions and method for HID communication
    internal static class NativeHid
    {
        #region HID & Kernel32 P/Invoke Definitions

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] // Use Unicode for paths
        internal static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes, // Use IntPtr.Zero for default
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);       // Use IntPtr.Zero for no template

        // Define necessary constants for CreateFile
        internal const uint GENERIC_READ = 0x80000000;
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_FLAG_OVERLAPPED = 0x40000000; // Often needed for HID

        // Define Product IDs to differentiate command formats
        public const ushort PID_STREAMDECK_MINI = 0x0063;
        public const ushort PID_STREAMDECK_MINI_V2 = 0x0090;
        // Add other PIDs if specific formats exist for them

        #endregion

        // Set Stream Deck Brightness via P/Invoke
        public static bool SetStreamDeckBrightness(string devicePath, ushort productId, byte brightnessPercentage)
        {
            if (string.IsNullOrEmpty(devicePath))
            {
                Console.WriteLine("Error: Device path cannot be null or empty.");
                return false;
            }
            brightnessPercentage = Math.Min(brightnessPercentage, (byte)100);

            SafeFileHandle? deviceHandle = null;
            bool success = false;

            try
            {
                deviceHandle = CreateFile(
                    devicePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                if (deviceHandle == null || deviceHandle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Error opening device handle for path '{devicePath}': {new Win32Exception(error).Message} (Code: {error})");
                    return false;
                }

                byte[] reportBuffer;
                int reportLength;

                if (productId == PID_STREAMDECK_MINI || productId == PID_STREAMDECK_MINI_V2)
                {
                    reportLength = 17;
                    reportBuffer = new byte[reportLength];
                    reportBuffer[0] = 0x05; reportBuffer[1] = 0x55; reportBuffer[2] = 0xAA;
                    reportBuffer[3] = 0xD1; reportBuffer[4] = 0x01; reportBuffer[5] = brightnessPercentage;
                }
                else
                {
                    reportLength = 32; // Default/Standard format
                    reportBuffer = new byte[reportLength];
                    reportBuffer[0] = 0x03; reportBuffer[1] = 0x08; reportBuffer[2] = brightnessPercentage;
                }

                success = HidD_SetFeature(deviceHandle, reportBuffer, reportLength);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Error sending SetFeature report: {new Win32Exception(error).Message} (Code: {error})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An exception occurred: {ex.Message}");
                success = false;
            }
            finally
            {
                if (deviceHandle != null && !deviceHandle.IsClosed)
                {
                    deviceHandle.Close(); // SafeFileHandle handles disposal
                }
            }
            return success;
        }
    }
}

// License Info From Original Source: https://github.com/dend/decksurf-sdk
//
// MIT License
// Copyright(c) 2021 Den Delimarsky
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify,
// merge, publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
// SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
// OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.