namespace MediaCluster.MediaAnalyzer.Models;

public class AudioStream
{
    public int Index { get; set; }
    public string? CodecName { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
    public string? ChannelLayoutDescription => GetChannelLayoutDescription();
    public int? BitsPerRawSample { get; set; }
    public string? Language { get; set; }
    public bool HearingImpaired { get; set; }
    public bool VisualImpaired { get; set; }
        
    private string? GetChannelLayoutDescription()
    {
        if (Channels == null)
            return null;
                
        return Channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            6 => "5.1 Surround",
            8 => "7.1 Surround",
            _ => $"{Channels} Channels"
        };
    }
}