#include "stdafx.h"
#include <stdio.h>
#include <stdlib.h>
#include <assert.h>
#include <crtdbg.h>
#include <iostream>

using namespace std;

extern "C"
{
#include <setupapi.h>
#include <Hidsdi.h>
}

#include <Dbt.h>
#include <tchar.h>
#include "virtualXbox.h"


// Interface Functions
extern "C"
{
	/// Status
	VXBOX_API BOOL __cdecl VBusExists(void)
	{
		TCHAR path[MAX_PATH];

		int n = GetVXbusPath(path, MAX_PATH);

		if (n > 0)
			return TRUE;
		else
			return FALSE;
	}

	VXBOX_API BOOL __cdecl GetEmptyBusSlotsCount(UCHAR * nSlots)
	{
		UCHAR output[1];
		DWORD trasfered = 0;


		if (g_hBus == INVALID_HANDLE_VALUE)
			g_hBus = GetVXbusHandle();
		if (g_hBus == INVALID_HANDLE_VALUE)
			return FALSE;

		// Send request to bus
		if (DeviceIoControl(g_hBus, IOCTL_BUSENUM_EMPTY_SLOTS, nullptr, 0, output, 1, &trasfered, nullptr))
		{
			*nSlots = *output;
			return TRUE;
		}

		return FALSE;
	}

	VXBOX_API BOOL __cdecl ControllerExists(UINT UserIndex)
	{
		BOOL out = FALSE;
		ULONG buffer[1];
		ULONG output[1];
		DWORD trasfered = 0;

		if (UserIndex < 1 || UserIndex>4)
			return out;

		if (g_hBus == INVALID_HANDLE_VALUE)
			g_hBus = GetVXbusHandle();
		if (g_hBus == INVALID_HANDLE_VALUE)
			return out;

		// Prepare the User Index for sending
		buffer[0] = UserIndex;

		// Send request to bus
		if (DeviceIoControl(g_hBus, IOCTL_BUSENUM_ISDEVPLUGGED, buffer, _countof(buffer), output, 4, &trasfered, nullptr))
		{
			if (*output != 0)
				out = TRUE;
		};

		return out;
	}

	VXBOX_API BOOL __cdecl IsControllerOwned(UINT UserIndex)
	{
		ULONG OrigProcID = 0;
		ULONG ThisProcID = 0;

		// Sanity Check
		if (UserIndex < 1 || UserIndex > 4)
			return FALSE;

		// Does controler exist?
		if (!ControllerExists(UserIndex))
			return FALSE;

		// Get ID of the process that created the controler?
		if (!GetCreateProcID(UserIndex, &OrigProcID) || !OrigProcID)
			return FALSE;

		// Get ID of current process
		ThisProcID = GetCurrentProcessId();
		if (!ThisProcID)
			return FALSE;

		// Compare
		if (ThisProcID != OrigProcID)
			return FALSE;

		return TRUE;

	}

	/// Virtual device Plug-In/Unplug

	VXBOX_API BOOL __cdecl PlugIn(UINT UserIndex)
	{
		BOOL out = FALSE;


		if (UserIndex < 1 || UserIndex>4)
			return out;

		if (g_hBus == INVALID_HANDLE_VALUE)
			g_hBus = GetVXbusHandle();
		if (g_hBus == INVALID_HANDLE_VALUE)
			return out;

		DWORD trasfered = 0;
		UCHAR buffer[16] = {};

		buffer[0] = 0x10;

		buffer[4] = ((UserIndex >> 0) & 0xFF);
		buffer[5] = ((UserIndex >> 8) & 0xFF);
		buffer[6] = ((UserIndex >> 16) & 0xFF);
		buffer[8] = ((UserIndex >> 24) & 0xFF);

		if (DeviceIoControl(g_hBus, IOCTL_BUSENUM_PLUGIN_HARDWARE, buffer, _countof(buffer), nullptr, 0, &trasfered, nullptr))
		{
			out = TRUE;
			g_vDevice[UserIndex - 1] = TRUE;
		};

		//CloseHandle(h);
		DWORD error = 0;
		if (out)
		{
			//std::cout << "IOCTL_BUSENUM_PLUGIN_HARDWARE 0X" << IOCTL_BUSENUM_PLUGIN_HARDWARE << "\n" << endl;
			error = 0;
		}
		else
		{
			error = GetLastError();
			//std::cout << "IOCTL_BUSENUM_PLUGIN_HARDWARE 0X" << IOCTL_BUSENUM_PLUGIN_HARDWARE << "Failed (Error:0X" << error << ")\n" << endl;
		}

		return out;
	}

	VXBOX_API BOOL __cdecl Unplug(UINT UserIndex, BOOL Force)
	{
		return UnPlug_Opt(UserIndex, Force);
	}

	/// Data Transfer (Feedback from the device)

	VXBOX_API BOOL __cdecl GetLedNumber(UINT UserIndex, PBYTE pLed)
	{
		BOOL ref = XOutputSetGetState(UserIndex, &g_Gamepad[UserIndex - 1], nullptr, nullptr, nullptr, pLed);
		if (ref)
			(*pLed)++;
		return ref;
	}

	VXBOX_API BOOL __cdecl GetVibration(UINT UserIndex, PXINPUT_VIBRATION pVib)
	{
		BYTE LargeMotor, SmallMotor, Vibrate;
		BOOL ref = XOutputSetGetState(UserIndex, &g_Gamepad[UserIndex - 1], &Vibrate, &LargeMotor, &SmallMotor, nullptr);
		if (ref)
		{
			if (Vibrate)
			{
				(*pVib).wLeftMotorSpeed = LargeMotor * 256;
				(*pVib).wRightMotorSpeed = SmallMotor * 256;
			}
			else
				(*pVib).wLeftMotorSpeed = (*pVib).wRightMotorSpeed = 0;
		};
		return ref;
	}

	VXBOX_API BOOL __cdecl GetState(UINT userIndex, XINPUT_GAMEPAD* gamepad)
	{
		return XOutputSetGetState(userIndex, gamepad, nullptr, nullptr, nullptr, nullptr);
	}

	/// Data Transfer (Data to the device)

	VXBOX_API BOOL __cdecl SetButton(UINT UserIndex, UINT Button, BOOL Press)
	{
		g_Gamepad[UserIndex - 1].wButtons &= ~Button;
		g_Gamepad[UserIndex - 1].wButtons |= Button*Press;
		return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
	}

	VXBOX_API BOOL __cdecl SetTrigger(UINT UserIndex, UINT Trigger, BYTE Value)
	{
		switch (Trigger)
		{
		case LeftTrigger:
			return SetTriggerL(UserIndex, Value);
		case RightTrigger:
			return SetTriggerR(UserIndex, Value);
		default:
			return false;
		}
	}

	VXBOX_API BOOL __cdecl SetDpad(UINT UserIndex, INT Value)
	{
		g_Gamepad[UserIndex - 1].wButtons &= 0xFFF0;
		g_Gamepad[UserIndex - 1].wButtons |= Value;
		return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
	}

	VXBOX_API BOOL __cdecl SetAxis(UINT UserIndex, UINT Axis, SHORT Value)
	{
		switch (Axis)
		{
		case XboxAxisX:
			return SetAxisX(UserIndex, Value);
		case XboxAxisY:
			return SetAxisY(UserIndex, Value);
		case XboxAxisRx:
			return SetAxisRx(UserIndex, Value);
		case XboxAxisRy:
			return SetAxisRy(UserIndex, Value);
		default:
			return false;
		}
	}
}

// Helper Functions

BOOL SetAxisX(UINT UserIndex, SHORT Value) // Left Stick X
{
	g_Gamepad[UserIndex - 1].sThumbLX = Value;
	return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
}

BOOL SetAxisY(UINT UserIndex, SHORT Value) // Left Stick X
{
	g_Gamepad[UserIndex - 1].sThumbLY = Value;
	return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
}

BOOL SetAxisRx(UINT UserIndex, SHORT Value) // Left Stick X
{
	g_Gamepad[UserIndex - 1].sThumbRX = Value;
	return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
}

BOOL SetAxisRy(UINT UserIndex, SHORT Value) // Left Stick X
{
	g_Gamepad[UserIndex - 1].sThumbRY = Value;
	return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
}

BOOL SetTriggerL(UINT UserIndex, BYTE Value) // Left Trigger
{
	g_Gamepad[UserIndex - 1].bLeftTrigger = Value;
	return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
}

BOOL SetTriggerR(UINT UserIndex, BYTE Value) // Right Trigger
{
	g_Gamepad[UserIndex - 1].bRightTrigger = Value;
	return XOutputSetState(UserIndex, &g_Gamepad[UserIndex - 1]);
}

int GetVXbusPath(LPCTSTR path, UINT size)
{
	SP_DEVICE_INTERFACE_DATA deviceInterfaceData = {};
	deviceInterfaceData.cbSize = sizeof(deviceInterfaceData);
	// GUID deviceClassGuid = { 0xF679F562, 0x3164, 0x42CE,{ 0xA4, 0xDB, 0xE7 ,0xDD ,0xBE ,0x72 ,0x39 ,0x09 } };
	GUID deviceClassGuid = GUID_DEVINTERFACE_SCPVBUS;
	DWORD memberIndex = 0;
	DWORD requiredSize = 0;

	auto deviceInfoSet = SetupDiGetClassDevs(&deviceClassGuid, nullptr, nullptr, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

	if (SetupDiEnumDeviceInterfaces(deviceInfoSet, nullptr, &deviceClassGuid, memberIndex, &deviceInterfaceData))
	{
		// get required target buffer size
		SetupDiGetDeviceInterfaceDetail(deviceInfoSet, &deviceInterfaceData, nullptr, 0, &requiredSize, nullptr);

		// allocate target buffer
		auto detailDataBuffer = static_cast<PSP_DEVICE_INTERFACE_DETAIL_DATA>(malloc(requiredSize));
		if (detailDataBuffer == NULL)
			return -1;
		detailDataBuffer->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA);

		// get detail buffer
		if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, &deviceInterfaceData, detailDataBuffer, requiredSize, &requiredSize, nullptr))
		{
			SetupDiDestroyDeviceInfoList(deviceInfoSet);
			free(detailDataBuffer);
			return -1;
		}

		// Copy	the path to output buffer
		memcpy((void *)path, detailDataBuffer->DevicePath, requiredSize*sizeof(WCHAR));

		// Cleanup
		SetupDiDestroyDeviceInfoList(deviceInfoSet);
		free(detailDataBuffer);
	}
	else
		return -1;

	return requiredSize;
}

HANDLE GetVXbusHandle(void)
{
	TCHAR path[MAX_PATH];

	int n = GetVXbusPath(path, MAX_PATH);

	if (n < 1)
		return INVALID_HANDLE_VALUE;

	// bus found, open it and obtain handle
	g_hBus = CreateFile(path,
		GENERIC_READ | GENERIC_WRITE,
		FILE_SHARE_READ | FILE_SHARE_WRITE,
		nullptr,
		OPEN_EXISTING,
		FILE_ATTRIBUTE_NORMAL,
		nullptr);
	return g_hBus;
}

BOOL GetCreateProcID(DWORD UserIndex, PULONG ProcID)
{
	BOOL out = FALSE;
	ULONG buffer[1];
	ULONG output[1];
	DWORD trasfered = 0;

	if (UserIndex < 1 || UserIndex>4)
		return out;

	if (g_hBus == INVALID_HANDLE_VALUE)
		g_hBus = GetVXbusHandle();
	if (g_hBus == INVALID_HANDLE_VALUE)
		return out;

	// Prepare the User Index for sending
	buffer[0] = UserIndex;

	// Send request to bus
	if (DeviceIoControl(g_hBus, IOCTL_BUSENUM_PROC_ID, buffer, _countof(buffer), output, 4, &trasfered, nullptr))
	{
		*ProcID = output[0];
		out = TRUE;
	};

	return out;

}

BOOL XOutputSetState(DWORD UserIndex, XINPUT_GAMEPAD* pGamepad)
{
	{
		BOOL out = FALSE;

		if (UserIndex < 1 || UserIndex>4)
			return out;

		if (!g_vDevice[UserIndex - 1])
			return out;

		DWORD trasfered = 0;
		BYTE buffer[28] = {};

		buffer[0] = 0x1C;

		// encode user index
		buffer[4] = ((UserIndex >> 0) & 0xFF);
		buffer[5] = ((UserIndex >> 8) & 0xFF);
		buffer[6] = ((UserIndex >> 16) & 0xFF);
		buffer[7] = ((UserIndex >> 24) & 0xFF);

		buffer[9] = 0x14;

		// concat gamepad info to buffer
		memcpy_s(&buffer[10], _countof(buffer), pGamepad, sizeof(XINPUT_GAMEPAD));

		// vibration and LED info end up here
		BYTE output[FEEDBACK_BUFFER_LENGTH] = {};

		// send report to bus, receive vibration and LED status
		if (!DeviceIoControl(g_hBus, IOCTL_BUSENUM_REPORT_HARDWARE, buffer, _countof(buffer), output, FEEDBACK_BUFFER_LENGTH, &trasfered, nullptr))
		{
			return FALSE;
		}

		// Save last successful position data
		memcpy_s(&g_Gamepad[UserIndex - 1], sizeof(XINPUT_GAMEPAD), pGamepad, sizeof(XINPUT_GAMEPAD));

		return TRUE;
	}
}

BOOL XOutputSetGetState(DWORD UserIndex, XINPUT_GAMEPAD * pGamepad, PBYTE bVibrate, PBYTE bLargeMotor, PBYTE bSmallMotor, PBYTE bLed)
{
	BOOL out = FALSE;

	if (UserIndex < 1 || UserIndex>4)
		return out;

	if (!g_vDevice[UserIndex - 1])
		return out;

	DWORD trasfered = 0;
	BYTE buffer[28] = {};

	buffer[0] = 0x1C;

	// encode user index
	buffer[4] = ((UserIndex >> 0) & 0xFF);
	buffer[5] = ((UserIndex >> 8) & 0xFF);
	buffer[6] = ((UserIndex >> 16) & 0xFF);
	buffer[7] = ((UserIndex >> 24) & 0xFF);

	buffer[9] = 0x14;

	// concat gamepad info to buffer
	memcpy_s(&buffer[10], _countof(buffer), pGamepad, sizeof(XINPUT_GAMEPAD));

	// vibration and LED info end up here
	BYTE output[FEEDBACK_BUFFER_LENGTH] = {};

	// send report to bus, receive vibration and LED status
	if (!DeviceIoControl(g_hBus, IOCTL_BUSENUM_REPORT_HARDWARE, buffer, _countof(buffer), output, FEEDBACK_BUFFER_LENGTH, &trasfered, nullptr))
	{
		return FALSE;
	}

	// Save last successful position data
	memcpy_s(&g_Gamepad[UserIndex - 1], sizeof(XINPUT_GAMEPAD), pGamepad, sizeof(XINPUT_GAMEPAD));

	// cache feedback
	if (bVibrate != nullptr)
	{
		*bVibrate = (output[1] == 0x08) ? 0x01 : 0x00;
	}

	if (bLargeMotor != nullptr)
	{
		*bLargeMotor = output[3];
	}

	if (bSmallMotor != nullptr)
	{
		*bSmallMotor = output[4];
	}

	if (bLed != nullptr)
	{
		*bLed = output[8];
	}

	return TRUE;
}

WORD ConvertButton(LONG vBtns, WORD xBtns, UINT vBtn, UINT xBtn)
{
	WORD out;
	out = ((vBtns&(1 << (vBtn - 1))) == 0) ? xBtns & ~xBtn : xBtns | xBtn;
	return out;
}

BOOL UnPlug_Opt(UINT UserIndex, BOOL Force)
{
	BOOL out = FALSE;

	if (UserIndex < 1 || UserIndex > 4)
		return out;

	if (g_hBus == INVALID_HANDLE_VALUE)
		g_hBus = GetVXbusHandle();
	if (g_hBus == INVALID_HANDLE_VALUE)
		return out;

	DWORD trasfered = 0;
	BUSENUM_UNPLUG_HARDWARE buffer = {};


	buffer.Size = sizeof(BUSENUM_UNPLUG_HARDWARE);
	buffer.SerialNo = UserIndex;

	if (Force)
		buffer.Flags = 0x0001;
	else
		buffer.Flags = 0x0000;

	if (DeviceIoControl(g_hBus, IOCTL_BUSENUM_UNPLUG_HARDWARE, (LPVOID)(&buffer), buffer.Size, nullptr, 0, &trasfered, nullptr))
	{
		out = TRUE;
		g_vDevice[UserIndex - 1] = FALSE;
	}

	return out;
}