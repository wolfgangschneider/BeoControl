namespace Beoported.Pc2;

/// <summary>
/// PC2 mixer/audio control: volume, speaker power, mute, audio routing.
/// Sends USB commands to the PC2 hardware.
/// </summary>
public class Pc2Mixer
{
    private readonly Pc2Device _device;

    /// <summary>Current audio parameters as reported by the hardware (updated by ProcessMixerState).</summary>
   // public AudioSetup AudioSetup { get; private set; } = new();
    public bool HeadphonesPluggedIn { get; private set; }
    public bool TransmittingLocally { get; private set; }
    public bool TransmittingFromMl { get; private set; }
    public bool DistributingOnMl { get; private set; }
    public bool SpeakersMuted { get; private set; }
    public bool SpeakersOn { get; private set; }

    public Pc2Mixer(Pc2Device device)
    {
        _device = device;
    }

    public void SpeakerMute(bool isMuted)
    {
        byte cmd = isMuted ? (byte)0x80 : (byte)0x81;
        _device.SendMessage([0xEA, cmd]);
        SpeakersMuted = isMuted;
    }

    public void SpeakerPower(bool isPowered)
    {
        byte cmd = isPowered ? (byte)0xFF : (byte)0x00;
        if (isPowered)
        {
            _device.SendMessage([0xEA, cmd]);
            SpeakerMute(false);
        }
        else
        {
            SpeakerMute(true);
            _device.SendMessage([0xEA, cmd]);
        }
        SpeakersOn = isPowered;
    }

    public void AdjustVolume(int adjustment)
    {
        if (adjustment == 0) return;
        int increment = adjustment > 0 ? 1 : -1;
        for (int i = 0; i != adjustment; i += increment)
        {
            _device.SendMessage([0xEB, adjustment > 0 ? (byte)0x80 : (byte)0x81]);
        }
    }

    public void SendRoutingState()
    {
        byte muted = (byte)((DistributingOnMl || TransmittingLocally || TransmittingFromMl) ? 0x00 : 0x01);
        byte distribute = (byte)(DistributingOnMl ? 0x01 : 0x00);

        byte locally;
        if (TransmittingLocally && TransmittingFromMl)
            locally = 0x03;
        else if (TransmittingFromMl)
            locally = 0x04;
        else if (TransmittingLocally)
            locally = 0x01;
        else
            locally = 0x00;

        _device.SendMessage([0xE7, muted]);
        _device.SendMessage([0xE5, locally, distribute, 0x00, muted]);
    }

    public void TransmitFromMl(bool enabled)
    {
        if (TransmittingFromMl != enabled)
        {
            TransmittingFromMl = enabled;
            SendRoutingState();
        }
    }

    public void TransmitLocally(bool enabled)
    {
        if (TransmittingLocally != enabled)
        {
            TransmittingLocally = enabled;
            SendRoutingState();
        }
    }

    public void MlDistribute(bool enabled)
    {
        if (DistributingOnMl != enabled)
        {
            DistributingOnMl = enabled;
            SendRoutingState();
        }
    }

    public void SetParameters(AudioSetup setup)
    {
        byte volByte = (byte)(setup.Volume | (setup.Loudness ? 0x80 : 0x00));
        _device.SendMessage([0xE3, volByte, (byte)setup.Bass, (byte)setup.Treble, (byte)setup.Balance]);
    }

    /// <summary>Process mixer state telegram from PC2 device.</summary>
    public AudioSetup? ProcessMixerState(byte[] tgram)
    {
        if (tgram.Length > 6)
        {
            AudioSetup ret = new AudioSetup
            {
                Volume   = (byte)(tgram[3] & 0x7F),
                Loudness = (tgram[3] & 0x80) != 0,
                Bass     = (sbyte)tgram[4],
                Treble   = (sbyte)tgram[5],
                Balance  = (sbyte)tgram[6],
            };
            return ret;
        }
        return null;
    }

    /// <summary>Process headphone state telegram from PC2 device.</summary>
    public void ProcessHeadphoneState(byte[] tgram)
    {
        if (tgram.Length > 3)
            HeadphonesPluggedIn = tgram[3] == 0x01;
    }
}
