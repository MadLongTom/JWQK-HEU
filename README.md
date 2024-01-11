# JWQK
 金智教务系统协议抢课
## Performance 
在23-24-2学期正选运行两天，总POST数超过40,000,000次，使用261个有效账号抢到370节课（与退课数量有关，在有其他脚本的情况下抢到比例>80%）
## Known Issues
### 金智运维会手动BAN IP，已将脚本升级为分布式代理模式，在此不发布，使用代理的思路是用*HttpClientHandler*添加
```csharp
 HttpClientHandler httpClientHandler = new HttpClientHandler()
 {
     Proxy = new WebProxy(proxies[procCount % proxies.Length]),
     UseProxy = true,
     ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
 };
 procCount += 1;
 HttpClient client = new(httpClientHandler)
 {
     Timeout = TimeSpan.FromSeconds(30)
 };
```
### 代理可能会被检测，目前最新思路是使用云服务商的闲置服务器跑脚本，几块钱几个小时，BAN就换。
### 此版本BatchID需要手动修改，自动化的思路是获取Login接口回执中的Data.Student.hrbeulcmap中的第一个元素的Key。
## Usage
在exe同目录下建立acc.txt，格式如下： 

```txt
acc pwd  网络 
acc pwd A,B,C 网络 
acc pwd  网络 
acc pwd  中国古建筑文化与鉴赏（网络）,如何赢得大学生创新创业大赛（网络） 
acc pwd  中国古建筑文化与鉴赏（网络）,如何赢得大学生创新创业大赛（网络） 
acc pwd  日语入门和朋辈心理辅导 
acc pwd A0,F  
acc pwd  网络 
acc pwd  四大名著的文化密码（网络）,朋辈心理辅导,艺术导论 
acc pwd  基础乐理,感悟考古（网络）,创践一大学生创新创业实务（网络） 
acc pwd A0,B,F 网络 
acc pwd A0.F 
acc pwd A,B,F 网络 
acc pwd  做自己：大学生职业生涯发展（网络）,交际礼仪与口才,现代战争启示录,创新工程实践（网络）,中国戏曲剧种鉴赏（网络） 
acc pwd A0 
acc pwd  网络 
acc pwd  创新和创业的理论与实践,创践—大学生创新创业实务（网络）,感悟考古（网络） 
acc pwd B,F 网络 
```

课程名为模糊匹配，类型与课程名为与关系。
## Dependencies
按需选择打码库，请手动实现ddddocr的IDisposable接口。 

ddddocr-cpu:https://github.com/zixing131/ddddocrsharp 

ddddocr-gpu:https://github.com/MadLongTom/ddddocr-Sharp-Ex
