// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ddddocrsharp;
using Yaap;

internal class Program
{
    static List<Task> tasks = [];
    static dddddocr ocr = new(show_ad: false);
    // CUDA环境(CUDA=11.6,cuDNN=8.5.0.96)
    private static void Main(string[] args)
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<string> list = [.. File.ReadAllLines("acc.txt")];
        foreach (var i in Enumerable.Range(0, list.Count).Yaap())
        {
            string[] spl = list[i].Split(' ');
            List<string> cat = spl.Length > 2 ? [.. spl[2].Split(',')] : [];
            cat.RemoveAll(x => x == string.Empty || x == "");
            List<string> cls = spl.Length > 3 ? [.. spl[3].Split(',')] : [];
            cls.RemoveAll(x => x == string.Empty || x == "");
            tasks.Add(Run(ocr, i, new Entity(spl[0], spl[1], cat, cls, [], false)));
        }
        Task.WaitAll([.. tasks]);
        sw.Stop();
        YaapConsole.WriteLine(sw.Elapsed.ToString());
    }
    static void ReportLoginState()
    {
        Manager.LoginCount++;
        if (Manager.LoginCount == tasks.Count - 1)
        {
            ocr.Dispose();
            GC.Collect();
        }
    }
    static async Task Run(dddddocr ocr, int serial, Entity entity)
    {
        try
        {
            var client = BuildInstance();
            Stopwatch stopwatch = Stopwatch.StartNew();
            loc_cap:
            var res = await Captcha(client);
            var captcha = await res.Content.ReadFromJsonAsync<CaptchaRoot>();
            var auth = ocr.classification(img_base64: captcha!.data.captcha.Split(',')[1]);
            YaapConsole.WriteLine($"{serial:D4}:{auth}");
            res = await Login(client, entity.username, AESEncrypt(entity.password), captcha!.data.uuid, auth);
            var ls = await res.Content.ReadFromJsonAsync<LoginRoot>();
            YaapConsole.WriteLine($"{entity.username}:{ls!.code}");
            if (ls!.code == 200)
            {
                ReportLoginState();
                client.DefaultRequestHeaders.Authorization = new(ls.data.token);
                client.DefaultRequestHeaders.Add("Cookie", $"Authorization={ls.data.token}");
                client.DefaultRequestHeaders.Add("batchId", "003acc046ba941eeb52513b3f8294432");
                await SelectCourseTillDie(client, entity);
                //await Task.Delay(10000);
            }
            else
            {
                goto loc_cap;
            }
            stopwatch.Stop();
            client.Dispose();
        }
        catch (Exception ex)
        {
            YaapConsole.WriteLine($"Retry {entity.username}" + Environment.NewLine + ex.GetType() + ":" + ex.Message);
            await Run(ocr, serial, entity);
        }
    }

    static HttpClient BuildInstance()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new("text/plain"));
        client.DefaultRequestHeaders.Accept.Add(new("*/*"));
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");
        client.DefaultRequestHeaders.Connection.Add("keep-alive");
        return client;
    }

    static string AESEncrypt(string text)
    {
        var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes("MWMqg2tPcDkxcm11");
        var cipher = aes.EncryptEcb(Encoding.UTF8.GetBytes(text), PaddingMode.PKCS7);
        return Convert.ToBase64String(cipher);
    }

    static async Task<HttpResponseMessage> Captcha(HttpClient client)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string> { });
        return await client.PostAsync("http://jwxk.hrbeu.edu.cn/xsxk/auth/captcha", content);
    }

    static async Task<HttpResponseMessage> Login(HttpClient client, string username, string password, string uuid, string auth)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string> {
            {"loginname",username },
            {"password",password },
            {"captcha",auth },
            {"uuid",uuid }
        });
        content.Headers.ContentType = new("application/x-www-form-urlencoded");
        var res = client.PostAsync("http://jwxk.hrbeu.edu.cn/xsxk/auth/hrbeu/login", content);
        return await res;
    }

    static async Task<List<Row>> GetAvailableClass(HttpClient client, Entity entity)
    {
        await Task.Delay(250);
        var urlList = "http://jwxk.hrbeu.edu.cn/xsxk/elective/clazz/list";
        var dataList = new Dictionary<string, object>
        {
            { "SFCT", "0" },
            //{ "XGXKLB",xgxklb["F"] },
            //{ "KEY","网络" },
            { "campus", "01" },
            { "orderBy", "" },
            { "pageNumber",1 },
            { "pageSize" , 200 },
            { "teachingClassType" , "XGKC" }
        };
        loc_resent1:
        var content = JsonContent.Create(dataList);
        content.Headers.ContentType = new("application/json")
        {
            CharSet = "UTF-8"
        };
        content.Headers.ContentLength = content.ReadAsStringAsync().Result.Length;
        HttpRequestMessage hrt = new(HttpMethod.Post, urlList);
        hrt.Headers.Referrer = new("http://jwxk.hrbeu.edu.cn/xsxk/elective/grablessons?batchId=003acc046ba941eeb52513b3f8294432");
        hrt.Headers.Host = "jwxk.hrbeu.edu.cn";
        hrt.Content = content;
        var responsePublicList = await client.SendAsync(hrt);
        ListRoot? classList;
        try
        {
            classList = await responsePublicList.Content.ReadFromJsonAsync<ListRoot>();
            YaapConsole.WriteLine($"{entity.username}:available={classList!.data.total}");
            return [.. classList!.data.rows];
        }
        catch (Exception ex)
        {
            if (responsePublicList.Content.ReadAsStringAsync().Result.Contains("请求过快")) goto loc_resent1;
            YaapConsole.WriteLine(await responsePublicList.Content.ReadAsStringAsync());
        }
        return [];
    }

    static async Task<bool> Add(HttpClient client, Entity entity, Row @class)
    {

        var addUrl = "http://jwxk.hrbeu.edu.cn/xsxk/elective/clazz/add";
        var addData = new Dictionary<string, string>
        {
            { "clazzType", "XGKC" },
            { "clazzId",@class.JXBID },
            { "secretVal",@class.secretVal },
            //{ "chooseVolunteer", "1" }
        };
        try
        {
            loc_resent:
            HttpRequestMessage hrt = new(HttpMethod.Post, addUrl);
            hrt.Headers.Referrer = new("http://jwxk.hrbeu.edu.cn/xsxk/elective/grablessons?batchId=003acc046ba941eeb52513b3f8294432");
            hrt.Headers.Host = "jwxk.hrbeu.edu.cn";
            hrt.Content = new FormUrlEncodedContent(addData);
            hrt.Content.Headers.ContentType = new("application/x-www-form-urlencoded");
            var addResponse = await client.SendAsync(hrt);
            try
            {
                var addContent = await addResponse.Content.ReadFromJsonAsync<AddRoot>();
                if (addContent!.code == 200)
                {
                    Console.WriteLine($"{entity.username}:{DateTime.Now:HH:mm:ss} {@class.KCM}");
                    try
                    {
                        File.AppendAllText("success.txt", $"{entity.username}:{DateTime.Now:HH:mm:ss} {@class.KCM}");
                    }
                    catch (Exception ex) { }
                    return true;
                }
                else
                {
                    if (addContent.msg.Contains("请求过快"))
                    {
                        goto loc_resent;
                    }
                    if (addContent.msg.Contains("已选满5门，不可再选")) { entity.finished = true; return true; }
                    //if (addContent.msg.Contains("选课结果中") || addContent.msg.Contains("不能重复选课")) { entity.done.Add(@class); }
                    Console.WriteLine($"{entity.username}:{@class.KCM} {addContent.msg}");
                    return false;
                }
            }
            catch (Exception e)
            {
                YaapConsole.WriteLine(await addResponse.Content.ReadAsStringAsync());
                return false;
            }
        }
        catch (Exception e)
        {
            YaapConsole.WriteLine(e.ToString());
            return false;
        }
    }

    static async Task SelectCourseSoft(HttpClient client, Entity entity)
    {
        while (true)
        {
            if (entity.finished) return;
            Dictionary<string, int>? xgxklb = new() { { "A", 12 }, { "B", 13 }, { "C", 14 }, { "D", 15 }, { "E", 16 }, { "F", 17 }, { "A0", 18 } };
            List<Row> publicList = (await GetAvailableClass(client, entity)).Where(p => p.classCapacity > p.numberOfSelected).ToList();
            List<Row> privateList = [];
            if (entity.classname.Count > 0 && entity.category.Count > 0)
            {
                privateList = publicList.Where(p => entity.classname.Any(q =>
                p.KCM.Contains(q)) && entity.category.Contains(xgxklb.First(t => p.XGXKLB.Contains(t.Key)).Key)).ToList();
            }
            else if (entity.classname.Count > 0)
            {
                privateList.AddRange(publicList.Where(p => entity.classname.Any(q => p.KCM.Contains(q))));
            }
            else if (entity.category.Count > 0)
            {
                privateList.AddRange(publicList.Where(p => entity.category.Contains(xgxklb.First(t => p.XGXKLB.Contains(t.Key)).Key)));
            }
            else
            {
                privateList = publicList;
            }
            privateList.RemoveAll(q => entity.done.Any(p => p.KCH == q.KCH));
            foreach (Row @class in privateList)
            {
                await Task.Delay(250);
                if (await Add(client, entity, @class))
                {
                    entity.done.Add(@class);
                }
            }
        }
    }
    static async Task SelectCourseTillDie(HttpClient client, Entity entity)
    {
        Dictionary<string, int>? xgxklb = new() { { "A", 12 }, { "B", 13 }, { "C", 14 }, { "D", 15 }, { "E", 16 }, { "F", 17 }, { "A0", 18 } };
        List<Row> publicList = await GetAvailableClass(client, entity);
        List<Row> privateList = [];
        if (entity.classname.Count > 0 && entity.category.Count > 0)
        {
            privateList = publicList.Where(p => entity.classname.Any(q =>
            p.KCM.Contains(q)) && entity.category.Contains(xgxklb.First(t => p.XGXKLB.Contains(t.Key)).Key)).ToList();
        }
        else if (entity.classname.Count > 0)
        {
            privateList.AddRange(publicList.Where(p => entity.classname.Any(q => p.KCM.Contains(q))));
        }
        else if (entity.category.Count > 0)
        {
            privateList.AddRange(publicList.Where(p => entity.category.Contains(xgxklb.First(t => p.XGXKLB.Contains(t.Key)).Key)));
        }
        else
        {
            privateList = publicList;
        }
        while (true)
        {
            if (entity.finished) return;
            privateList.RemoveAll(q => entity.done.Any(p => p.KCH == q.KCH));
            foreach (Row @class in privateList)
            {
                await Task.Delay(250);
                if (await Add(client, entity, @class))
                {
                    entity.done.Add(@class);
                }
            }
        }
    }
}


public class AddRoot
{
    public int code { get; set; }
    public string msg { get; set; }
    public object data { get; set; }
}
public class CaptchaRoot
{
    public int code { get; set; }
    public string msg { get; set; }
    public Data data { get; set; }
}
public partial class Data
{
    public string captcha { get; set; }
    public string type { get; set; }
    public string uuid { get; set; }
}
public class LoginRoot
{
    public int code { get; set; }
    public string msg { get; set; }
    public Data data { get; set; }
}
public partial class Data
{
    public string token { get; set; }
    public Student student { get; set; }
}
public class Student
{
    public Hrbeulcmap hrbeuLcMap { get; set; }
    public string XH { get; set; }
    public string XM { get; set; }
    public string gender { get; set; }
    public string college { get; set; }
    public string YXMC { get; set; }
    public string major { get; set; }
    public string ZYMC { get; set; }
    public object ZYLB { get; set; }
    public object majorDirection { get; set; }
    public object majorDirectionName { get; set; }
    public string lengthOfSchool { get; set; }
    public object headImageUrl { get; set; }
    public string NJ { get; set; }
    public string schoolClass { get; set; }
    public string schoolClassName { get; set; }
    public object virtualClasses { get; set; }
    public string campus { get; set; }
    public string campusName { get; set; }
    public string teachCampus { get; set; }
    public string ZXF { get; set; }
    public string YXXF { get; set; }
    public object levelTeaching { get; set; }
    public string trainingLevel { get; set; }
    public string studentType { get; set; }
    public object studentType2 { get; set; }
    public object specialStudentType { get; set; }
    public object studentTag { get; set; }
    public object origin { get; set; }
    public object emigrant { get; set; }
    public string majorTrainingCode { get; set; }
    public object minorTrainingCode { get; set; }
    public object extendTrainingCode { get; set; }
    public object limitElectiveMap { get; set; }
    public Electivebatchlist[] electiveBatchList { get; set; }
    public string noSearchCj { get; set; }
    public object historyStudyResult { get; set; }
    public object lscjMap { get; set; }
    public string hasInfo { get; set; }
    public int infoCount { get; set; }
    public object sfz { get; set; }
    public object yearCampusMap { get; set; }
    public string ZSXDXF { get; set; }
    public object xsbh { get; set; }
    public object xgxkxz { get; set; }
}
public class Hrbeulcmap
{
    public _003Acc046ba941eeb52513b3f8294432 _003acc046ba941eeb52513b3f8294432 { get; set; }
}
public class _003Acc046ba941eeb52513b3f8294432
{
    public string noLimitJxb { get; set; }
    public Studentmenu studentMenu { get; set; }
}
public class Studentmenu
{
    public string electiveBatchCode { get; set; }
    public string grade { get; set; }
    public string college { get; set; }
    public object major { get; set; }
    public object schoolClass { get; set; }
    public object studentType { get; set; }
    public object studentType2 { get; set; }
    public object lengthOfSchool { get; set; }
    public string noCrossLevel { get; set; }
    public string fawSelectSelfCollegeCourse { get; set; }
    public string fawSelectCollegeGradeCourse { get; set; }
    public object fawSelectGroupCourse { get; set; }
    public string displayTJKC { get; set; }
    public string displayFANKC { get; set; }
    public string displayFAWKC { get; set; }
    public string displayCXKC { get; set; }
    public string displayTYKC { get; set; }
    public string displayXGKC { get; set; }
    public string displayFXKC { get; set; }
    public string displayALLKC { get; set; }
}
public class Electivebatchlist
{
    public string noDisplaySelectedNumber { get; set; }
    public string code { get; set; }
    public string name { get; set; }
    public object noSelectReason { get; set; }
    public object noSelectCode { get; set; }
    public string canSelect { get; set; }
    public string schoolTerm { get; set; }
    public string beginTime { get; set; }
    public string endTime { get; set; }
    public string tacticCode { get; set; }
    public string tacticName { get; set; }
    public string typeCode { get; set; }
    public string typeName { get; set; }
    public string needConfirm { get; set; }
    public string confirmInfo { get; set; }
    public string isConfirmed { get; set; }
    public string schoolTermName { get; set; }
    public string weekRange { get; set; }
    public string canSelectBook { get; set; }
    public string canDeleteBook { get; set; }
    public string multiCampus { get; set; }
    public string multiTeachCampus { get; set; }
    public object menuList { get; set; }
    public string noCheckTimeConflict { get; set; }
}
public class ListRoot
{
    public int code { get; set; }
    public string msg { get; set; }
    public Data data { get; set; }
}
public partial class Data
{
    public int total { get; set; }
    public Row[] rows { get; set; }
}
public partial class Row
{
    public string KCH { get; set; }
    public string KCM { get; set; }
    public int BJS { get; set; }
    public string KCLB { get; set; }
    public string hours { get; set; }
    public string XF { get; set; }
    public string SFYX { get; set; }
    public string KKDW { get; set; }
    public string KCXZ { get; set; }
    public Tclist[] tcList { get; set; }
    public object courseUrl { get; set; }
    public object ZFX { get; set; }
    public object CXCKLX { get; set; }
    public object KCLY { get; set; }
    public object TDGX { get; set; }
    public string KSLX { get; set; }
}
public class Tclist
{
    public string teachCampus { get; set; }
    public string SFXZXB { get; set; }
    public string isRetakeClass { get; set; }
    public string department { get; set; }
    public string TJBJ { get; set; }
    public int NSKRL { get; set; }
    public int NVSKRL { get; set; }
    public int NSXKRS { get; set; }
    public int NVSXKRS { get; set; }
    public SKSJ[] SKSJ { get; set; }
    public string schoolTerm { get; set; }
    public string JXBID { get; set; }
    public string campus { get; set; }
    public string XQ { get; set; }
    public string KCH { get; set; }
    public string KCM { get; set; }
    public string KXH { get; set; }
    public string SKJS { get; set; }
    public string SKJSLB { get; set; }
    public string KKDW { get; set; }
    public string teachingPlace { get; set; }
    public string teachingPlaceHide { get; set; }
    public string XS { get; set; }
    public string XF { get; set; }
    public string examType { get; set; }
    public string hasTest { get; set; }
    public string isTest { get; set; }
    public string hasBook { get; set; }
    public int numberOfSelected { get; set; }
    public int numberOfFirstVolunteer { get; set; }
    public int classCapacity { get; set; }
    public string KCXZ { get; set; }
    public string KCLB { get; set; }
    public string courseType { get; set; }
    public string courseNature { get; set; }
    public string schoolClassMapStr { get; set; }
    public string SKJSZC { get; set; }
    public int KRL { get; set; }
    public int YXRS { get; set; }
    public int DYZYRS { get; set; }
    public string SFYX { get; set; }
    public string SFYM { get; set; }
    public string secretVal { get; set; }
    public string SFCT { get; set; }
    public string SFXZXK { get; set; }
    public string XGXKLB { get; set; }
    public string DGJC { get; set; }
    public string SFKT { get; set; }
    public string conflictDesc { get; set; }
    public string testTeachingClassID { get; set; }
    public string YPSJDD { get; set; }
    public string ZYDJ { get; set; }
    public string KSLX { get; set; }
    public string XDFS { get; set; }
    public string SFSC { get; set; }
}
public partial class SKSJ
{
    public string teachingClassID { get; set; }
    public string KCH { get; set; }
    public string KCM { get; set; }
    public string SKZC { get; set; }
    public string SKZCMC { get; set; }
    public string SKXQ { get; set; }
    public string KSJC { get; set; }
    public string JSJC { get; set; }
    public string timeType { get; set; }
    public string YPSJDD { get; set; }
    public string KXH { get; set; }
    public string SKJS { get; set; }
}
public partial class Row
{
    public string teachCampus { get; set; }
    public string SFXZXB { get; set; }
    public string isRetakeClass { get; set; }
    public string department { get; set; }
    public string courseGroupCode { get; set; }
    public int NSKRL { get; set; }
    public int NVSKRL { get; set; }
    public int NSXKRS { get; set; }
    public int NVSXKRS { get; set; }
    public SKSJ[] SKSJ { get; set; }
    public string schoolTerm { get; set; }
    public string JXBID { get; set; }
    public string campus { get; set; }
    public string XQ { get; set; }
    public string KXH { get; set; }
    public string SKJS { get; set; }
    public string SKJSLB { get; set; }
    public string teachingPlace { get; set; }
    public string teachingPlaceHide { get; set; }
    public string XS { get; set; }
    public string examType { get; set; }
    public string hasTest { get; set; }
    public string isTest { get; set; }
    public string hasBook { get; set; }
    public int numberOfSelected { get; set; }
    public int numberOfFirstVolunteer { get; set; }
    public int classCapacity { get; set; }
    public string courseType { get; set; }
    public string courseNature { get; set; }
    public string schoolClassMapStr { get; set; }
    public string SKJSZC { get; set; }
    public int KRL { get; set; }
    public int YXRS { get; set; }
    public int DYZYRS { get; set; }
    public string SFYM { get; set; }
    public string secretVal { get; set; }
    public string SFCT { get; set; }
    public string SFXZXK { get; set; }
    public string XGXKLB { get; set; }
    public string DGJC { get; set; }
    public string SFKT { get; set; }
    public string conflictDesc { get; set; }
    public string testTeachingClassID { get; set; }
    public string YPSJDD { get; set; }
    public string ZYDJ { get; set; }
    public string XDFS { get; set; }
    public string SFSC { get; set; }
}
public record Entity(string username, string password, List<string> category, List<string> classname, List<Row> done, bool finished)
{
    public bool finished { get; set; } = finished;
};
public static class Manager
{
    public static int LoginCount = 0;
}