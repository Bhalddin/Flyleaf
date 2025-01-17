﻿using System;
using System.Linq;

using Vortice.MediaFoundation;

namespace FlyleafLib.MediaFramework.MediaDevice;

public class VideoDevice : DeviceBase
{
    public VideoDevice(string friendlyName, string symbolicLink) : base(friendlyName, symbolicLink)
    {
        Streams = VideoDeviceStream.GetVideoFormatsForVideoDevice(friendlyName, symbolicLink);
        Url = Streams.OfType<VideoDeviceStream>().Where(f => f.SubType.Contains("MJPG") && f.FrameRate >= 30).OrderByDescending(f => f.FrameSizeHeight).FirstOrDefault().Url;
    }

    public static void RefreshDevices()
    {
        Utils.UIInvokeIfRequired(() =>
        {
            Engine.Video.CapDevices.Clear();

            var devices = MediaFactory.MFEnumVideoDeviceSources();
                foreach (var device in devices)
                try { Engine.Video.CapDevices.Add(new VideoDevice(device.FriendlyName, device.SymbolicLink)); } catch(Exception) { }
        });
    }
}
