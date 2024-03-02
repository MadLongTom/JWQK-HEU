// See https://aka.ms/new-console-template for more information
#pragma warning disable CS8618
public class Data
{
    public string captcha { get; set; }
    public string type { get; set; }
    public string uuid { get; set; }
    public string token { get; set; }
    public Student student { get; set; }
    public int total { get; set; }
    public Row[] rows { get; set; }
}
