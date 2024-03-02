using ddddocrsharp;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

internal class Program
{
    static readonly List<Task> ClaimTaskPool = [];
    static List<Entity> Entities = [];
    static readonly DdddOcr Ocr = new(show_ad: false, use_gpu: false);
    static readonly int ThreadID = Random.Shared.Next(1000, 9999);
    static int NetworkOverSpeedCounter = 0;
    static int NetworkLossCounter = 0;
    public static int NetworkSendCounter = 0;
    static int NetworkDelayCounter = 0;
    public static TimeSpan TotalTimeSpan = TimeSpan.Zero;
    static TimeSpan TotalDelayTimeSpan = TimeSpan.Zero;
    static readonly Aes AesUtil = Aes.Create();

    /// <summary>
    /// 程序主入口
    /// </summary>
    /// <returns>void</returns>
    private static async Task Main()
    {
        //设置TLS协议
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        //初始化AES密钥
        AesUtil.Key = Encoding.UTF8.GetBytes("MWMqg2tPcDkxcm11");
        //如果在非选课时间启动，等待到选课系统启动
        DateTime dt = DateTime.Parse("06:00:00");
        TimeSpan span = dt - DateTime.Now;
        if (span < TimeSpan.Zero && DateTime.Now.Hour >= 22) span += TimeSpan.FromDays(1);
        if (span > TimeSpan.Zero) Thread.Sleep(span);
        //处理用户数据
        List<string> list = [.. File.ReadAllLines("acc.txt")];
        foreach (var i in Enumerable.Range(0, list.Count))
        {
            string[] spl = list[i].Split(' ');
            List<string> cat = spl.Length > 2 ? [.. spl[2].Split(',')] : [];
            cat.RemoveAll(x => x == string.Empty || x == "");
            List<string> cls = spl.Length > 3 ? [.. spl[3].Split(',')] : [];
            cls.RemoveAll(x => x == string.Empty || x == "");
            Entities.Add(new Entity(spl[0], spl[1], cat, cls, [], false, spl.Length > 4 ? spl[4] : "0", null));
        }
        Entities = [.. Entities.DistinctBy(x => x.username)];
        //启动仪表板
        await Task.Factory.StartNew(async () =>
        {
            while (true)
            {
                await Task.Delay(1000);
                Console.Title = $"epoch={(NetworkSendCounter == 0 ? "0" : NetworkSendCounter.ToString() + " avg=" + (TotalTimeSpan / NetworkSendCounter).TotalMilliseconds.ToString() + "ms")} thread={ClaimTaskPool.Count(t => !t.IsCompleted)}/{ClaimTaskPool.Count} overSpeed={(NetworkSendCounter == 0 ? 0 : (NetworkOverSpeedCounter * 100 / NetworkSendCounter))}% loss={(NetworkSendCounter == 0 ? 0 : (NetworkLossCounter * 100 / NetworkSendCounter))}% delay={(NetworkDelayCounter == 0 ? "0" : NetworkDelayCounter.ToString() + " davg=" + (TotalDelayTimeSpan / NetworkDelayCounter).TotalMilliseconds.ToString() + "ms")}";
                NetworkSendCounter = 0;
                NetworkOverSpeedCounter = 0;
                NetworkLossCounter = 0;
                NetworkDelayCounter = 0;
                TotalDelayTimeSpan = TimeSpan.Zero;
                TotalTimeSpan = TimeSpan.Zero;
            }
        });
        //启动流程
        foreach (var i in Enumerable.Range(0, Entities.Count))
        {
            await Task.Delay(300);
            ClaimTaskPool.Add(Run(Ocr, i, Entities[i]));
        }
        //等待所有线程结束
        Task.WaitAll([.. ClaimTaskPool]);
    }
    /// <summary>
    /// 单用户流程
    /// </summary>
    /// <param name="ocr">验证码识别实例</param>
    /// <param name="serial">逻辑序号，无实际作用</param>
    /// <param name="entity">用户实例</param>
    /// <returns>void</returns>
    static async Task Run(DdddOcr ocr, int serial, Entity entity)
    {
        HttpResponseMessage? res;
        try
        {
            //创建客户端
            using var client = BuildClient();
            //若用户掉线，需要清除完成状态
            entity.finished = false;
            //登录失败重试标识
            loc_cap:
            //取验证数据
            res = await Captcha(client);
            //反序列化接口返回结果
            var captcha = await res.Content.ReadFromJsonAsync<CaptchaRoot>();
            //识别验证码
            var auth = ocr.classification(img_base64: captcha!.data.captcha.Split(',')[1]);
            Console.WriteLine($"{serial:D4}:{auth}");
            //登录
            res = await Login(client, entity.username, AESEncrypt(entity.password), captcha!.data.uuid, auth);
            //反序列化接口返回结果
            var ls = await res.Content.ReadFromJsonAsync<LoginRoot>();
            //若密码错误，此线程退出
            if (ls!.msg.Contains("密码错误")) { _ = WriteFileAsync("error.txt", ThreadID.ToString() + entity.ToString()); return; }
            Console.WriteLine($"{entity.username}:{ls!.code}");
            //验证登录结果
            if (ls!.code == 200)
            {
                //登录成功，设置请求头
                entity.batchId = ls.data.student.hrbeuLcMap.First().Key;
                client.DefaultRequestHeaders.Authorization = new(ls.data.token);
                client.DefaultRequestHeaders.Add("Cookie", $"Authorization={ls.data.token}");
                client.DefaultRequestHeaders.Add("batchId", entity.batchId);
                //进入不同选课模式
                if (GetPrivateList(await GetRowList(client, entity), entity).Count > 2) await QueryClaim(client, entity);
                else await DirectClaim(client, entity);
            }
            else
            {
                //时间未到，重试
                if (ls.msg.Contains("不在本次")) return;
                goto loc_cap;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Retry {entity.username}" + Environment.NewLine + ex.GetType() + ":" + ex.Message);
            await Run(ocr, serial, entity);
        }
    }
    /// <summary>
    /// 创建客户端
    /// </summary>
    /// <param name="proxy">代理（可空）</param>
    /// <returns>客户端实例</returns>
    static HttpClient BuildClient(WebProxy? proxy = null)
    {
        HttpClient client;
        if (proxy != null)
        {
            HttpClientHandler httpClientHandler = new()
            {
                Proxy = proxy,
                UseProxy = true,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            client = new(httpClientHandler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
        else client = new();
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
    /// <summary>
    /// 创建Post请求
    /// </summary>
    /// <param name="url">目标网址</param>
    /// <param name="entity">用户实例</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="content">内容</param>
    /// <returns>接口请求信息</returns>
    static HttpRequestMessage BuildPostRequest(string url, Entity entity, MediaTypeHeaderValue? contentType, HttpContent content)
    {
        HttpRequestMessage hrt = new(HttpMethod.Post, url);
        hrt.Headers.Referrer = new($"https://jwxk.hrbeu.edu.cn/xsxk/elective/grablessons?batchId={entity.batchId}");
        hrt.Headers.Host = "jwxk.hrbeu.edu.cn";
        hrt.Content = content;
        if (contentType != null) hrt.Content.Headers.ContentType = contentType;
        return hrt;
    }
    /// <summary>
    /// 接口密码加密
    /// </summary>
    /// <param name="text">原文</param>
    /// <returns>密文</returns>
    static string AESEncrypt(string text)
    {
        var cipher = AesUtil.EncryptEcb(Encoding.UTF8.GetBytes(text), PaddingMode.PKCS7);
        return Convert.ToBase64String(cipher);
    }
    /// <summary>
    /// 取验证数据
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <returns>接口返回信息</returns>
    static async Task<HttpResponseMessage> Captcha(HttpClient client)
    {
        var content = new FormUrlEncodedContent([]);
        content.Headers.ContentLength = 0;
        return await client.PostAsync("https://jwxk.hrbeu.edu.cn/xsxk/auth/captcha", content);
    }
    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="username">账号</param>
    /// <param name="password">密码</param>
    /// <param name="uuid">验证接口的唯一标识符</param>
    /// <param name="auth">验证码</param>
    /// <returns>接口返回信息</returns>
    static async Task<HttpResponseMessage> Login(HttpClient client, string username, string password, string uuid, string auth)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string> {
            {"loginname",username },
            {"password",password },
            {"captcha",auth },
            {"uuid",uuid }
        });
        content.Headers.ContentType = new("application/x-www-form-urlencoded");
        var res = client.PostAsync("https://jwxk.hrbeu.edu.cn/xsxk/auth/hrbeu/login", content);
        return await res;
    }

    static readonly string listUrl = "https://jwxk.hrbeu.edu.cn/xsxk/elective/clazz/list";
    static readonly Dictionary<string, object> listData = new()
    {
            { "SFCT", "0" },
            //{ "XGXKLB",xgxklb["F"] }, //global filter
            //{ "KEY","网络" },
            { "campus", "01" },
            { "orderBy", "" },
            { "pageNumber",1 },
            { "pageSize" , 300 },
            { "teachingClassType" , "XGKC" }
    };
    /// <summary>
    /// 获取课程列表
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="entity">用户实例</param>
    /// <returns>课程列表</returns>
    static async Task<List<Row>> GetRowList(HttpClient client, Entity entity)
    {
        //超速重发标识
        loc_resent1:
        //构造负载
        var content = JsonContent.Create(listData);
        content.Headers.ContentType = new("application/json")
        {
            CharSet = "UTF-8"
        };
        content.Headers.ContentLength = content.ReadAsStringAsync().Result.Length;
        HttpRequestMessage hrt = BuildPostRequest(listUrl, entity, null, content);
        //发送请求
        var responsePublicList = await client.LimitSendAsync(hrt, entity);
        ListRoot? classList;
        try
        {
            //反序列化接口结果
            classList = await responsePublicList.Content.ReadFromJsonAsync<ListRoot>();
            if(classList!.data.rows.Any(q => q.classCapacity > q.numberOfSelected)) 
                Console.WriteLine($"{entity.username}:available={classList!.data.total}");
            return [.. classList!.data.rows];
        }
        catch (Exception ex)
        {
            if (responsePublicList.Content.ReadAsStringAsync().Result.Contains("请求过快"))
            {
                NetworkOverSpeedCounter += 1;
                goto loc_resent1;
            }
            Console.WriteLine(entity.username + ":Error at List:" + await responsePublicList.Content.ReadAsStringAsync() + Environment.NewLine + ex);
        }
        return [];
    }

    static readonly string addUrl = "https://jwxk.hrbeu.edu.cn/xsxk/elective/clazz/add";
    /// <summary>
    /// 添加课程
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="entity">用户实例</param>
    /// <returns>添加结果</returns>
    static async Task<bool> Add(HttpClient client, Entity entity, Row @class)
    {
        //构造请求
        var addData = new Dictionary<string, string>
        {
            { "clazzType", "XGKC" },
            { "clazzId",@class.JXBID },
            { "secretVal",@class.secretVal },
            //{ "chooseVolunteer", "1" }  //正选不传
        };
        try
        {
            //超速重发标识
            loc_resent:
            HttpRequestMessage hrt = BuildPostRequest(addUrl, entity, new("application/x-www-form-urlencoded"), new FormUrlEncodedContent(addData));
            //发送请求
            var addResponse = await client.LimitSendAsync(hrt, entity);
            try
            {
                //反序列化接口信息
                var addContent = await addResponse.Content.ReadFromJsonAsync<AddRoot>();
                if (addContent!.code == 200)
                {
                    //接口返回添加成功，待确认
                    Console.WriteLine($"{entity.username}:Reported {@class.KCM}");
                    try
                    {
                        if (await ValidateClaim(client, entity, @class))
                        {
                            //确认添加成功
                            entity.done.Add(@class);
                            Console.WriteLine($"{entity.username}:Verified {@class.KCM}");
                            await WriteFileAsync("success.txt", $"{entity.username}:{DateTime.Now} {@class} \r\n");
                            return true;
                        };
                    }
                    catch (Exception ex) { await Console.Out.WriteLineAsync(ex.ToString()); }
                    return false;
                }
                else
                {
                    if (addContent.msg.Contains("请求过快")) { NetworkOverSpeedCounter += 1; goto loc_resent; }
                    if (addContent.msg.Contains("已选满5门，不可再选")) { entity.finished = true; return true; }
                    if (addContent.msg.Contains("容量已满")) return false;
                    if (addContent.msg.Contains("选课结果中") || addContent.msg.Contains("不能重复选课")) { return false; };
                    if (addContent.msg.Contains("学分超过")) { entity.finished = true; return false; }
                    if (addContent.msg.Contains("冲突")) { entity.done.Add(@class); return false; }
                    if (addContent.msg.Contains("请重新登录"))
                    {
                        //重连，需要重构
                        entity.finished = true;
                        await Task.Factory.StartNew(async () =>
                        {
                            await Task.Delay(3000);
                            ClaimTaskPool.Add(Run(Ocr, Random.Shared.Next(1000, 9999), entity));
                        });
                    }
                    Console.WriteLine($"{entity.username}:{@class.KCM} {addContent.msg}");
                    return false;
                }
            }
            catch (Exception e)
            {

                if (string.IsNullOrWhiteSpace(addResponse.Content.ReadAsStringAsync().Result))
                {
                    NetworkLossCounter++;
                    return false;
                }
                Console.WriteLine(entity.username + ":Error at Add:" + await addResponse.Content.ReadAsStringAsync() + Environment.NewLine + e);
                return false;
            }
        }
        catch (Exception e)
        {
            if (e is not HttpRequestException) Console.WriteLine(e.ToString());
            return false;
        }
    }

    static readonly string selectUrl = "https://jwxk.hrbeu.edu.cn/xsxk/elective/hrbeu/select";
    static readonly Dictionary<string, string> selectData = new()
    {
            { "jxblx","YXKCYX_XGKC"}
    };
    /// <summary>
    /// 由于金智服务器后端数据不同步，需要校验返回结果的真实性
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="entity">用户实例</param>
    /// <param name="class">接口返回的课程</param>
    /// <returns>真实结果</returns>
    static async Task<bool> ValidateClaim(HttpClient client, Entity entity, Row @class)
    {
        //构造请求
        HttpRequestMessage hrt = BuildPostRequest(selectUrl, entity, new("application/x-www-form-urlencoded"), new FormUrlEncodedContent(selectData));
        //发送请求
        var selectResponse = await client.LimitSendAsync(hrt, entity);
        //反序列化接口信息
        var selectContent = await selectResponse.Content.ReadFromJsonAsync<SelectRoot>();
        //如果已到选课上限，线程退出
        if (selectContent!.data.Count == 5) { entity.finished = true; await Console.Out.WriteLineAsync(entity.username + ":finished"); }
        await Console.Out.WriteLineAsync($"{entity.username}:selected count={selectContent!.data.Count}");
        return selectContent!.data.Any(q => q.KCH == @class.KCH);
    }

    static readonly Dictionary<string, int> xgxklb = new() { { "A", 12 }, { "B", 13 }, { "C", 14 }, { "D", 15 }, { "E", 16 }, { "F", 17 }, { "A0", 18 } };
    /// <summary>
    /// 请求空闲课程列表后添加课程，在欲选课程较多时此方法效率较高
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="entity">用户实例</param>
    /// <returns>void</returns>
    static async Task QueryClaim(HttpClient client, Entity entity)
    {
        while (true)
        {
            if (entity.finished) return;
            List<Row> publicList = await GetRowList(client, entity);
            List<Row> privateList = GetPrivateList(publicList, entity);
            privateList = privateList.Where(p => p.classCapacity > p.numberOfSelected).ToList();
            if (privateList.Count > 0)
                foreach (Row @class in privateList)
                {
                    await Add(client, entity, @class);
                }
        }
    }
    /// <summary>
    /// 直接选择课程，此方法仅获取一次课程列表，一直发送添加课程请求。在欲选课程较少时此方法效率较高
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="entity">用户实例</param>
    /// <returns>void</returns>
    static async Task DirectClaim(HttpClient client, Entity entity)
    {
        List<Row> publicList = await GetRowList(client, entity);
        List<Row> privateList = GetPrivateList(publicList, entity);
        while (true)
        {
            privateList.RemoveAll(q => entity.done.Any(p => p.KCH == q.KCH));
            if (privateList.Count == 0) { return; }
            foreach (Row @class in privateList)
            {
                if (entity.finished) return;
                await Add(client, entity, @class);
            }
        }
    }
    /// <summary>
    /// 获取筛选课程列表
    /// </summary>
    /// <param name="publicList">完整课程列表</param>
    /// <param name="entity">用户实例</param>
    /// <returns>筛选课程列表</returns>
    static List<Row> GetPrivateList(List<Row> publicList, Entity entity)
    {
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
        return privateList;
    }

    private static readonly SemaphoreSlim writeLock = new(1, 1);
    /// <summary>
    /// 线程安全的异步写文件
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="text">写入内容</param>
    /// <returns>void</returns>
    public static async Task WriteFileAsync(string path, string text)
    {
        await writeLock.WaitAsync();
        try
        {
            using StreamWriter sw = new(path, true);
            await sw.WriteLineAsync(text);
        }
        finally
        {
            writeLock.Release();
        }
    }
    /// <summary>
    /// 等待直到最低要求时间，若时间超过立即返回
    /// </summary>
    /// <param name="sw">计时器</param>
    /// <param name="requiredMillSeconds">最低要求时间</param>
    /// <returns>void</returns>
    public static async Task DelayTillLimit(Stopwatch sw, int requiredMillSeconds)
    {
        if (sw.ElapsedMilliseconds < requiredMillSeconds)
        {
            await Task.Delay(Convert.ToInt32(requiredMillSeconds - sw.ElapsedMilliseconds));
        }
        else if (sw.ElapsedMilliseconds > requiredMillSeconds)
        {
            TotalDelayTimeSpan += new TimeSpan(0, 0, 0, 0, Convert.ToInt32(sw.ElapsedMilliseconds - requiredMillSeconds));
            NetworkDelayCounter += 1;
        }
    }
}

public static class HttpClientExtensions
{
    static int LimitMillSeconds = 350;
    public static async Task<HttpResponseMessage> LimitSendAsync(this HttpClient client, HttpRequestMessage hrm, Entity entity)
    {
        await Program.DelayTillLimit(entity.stopwatch, LimitMillSeconds);
        entity.stopwatch.Restart();
        var res = await client.SendAsync(hrm);
        Program.NetworkSendCounter += 1;
        Program.TotalTimeSpan += entity.stopwatch.Elapsed;
        return res;
    }
}