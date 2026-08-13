# 禁漫天堂下载器（.NET 版）

基于 [jmcomic-downloader](https://github.com/lanyeeee/jmcomic-downloader)（Rust/Tauri 版）重置的 .NET 实现。用于 18comic.vip 禁漫天堂的多线程漫画下载器，带图形界面与收藏夹。

## 功能

- 漫画搜索：关键词 / JM 号，支持按最新、点击、图片数、爱心排序与分页
- 漫画收藏：登录后查看收藏夹，支持文件夹切换、排序、分页与收藏夹同步
- 章节下载：勾选 / 左键拖动框选 / Ctrl 多选 / 右键菜单，一键下载所有章节
- 多线程下载：最多同时获取 10 个章节图片列表、下载 3 个章节、40 张图片
- 实时进度：全局进度条、每章节进度、下载速度
- 图片格式：jpg / png / webp 可选，自动重组禁漫分块乱序图片
- 断点续传：已下载/已存在的图片自动跳过，中断后重新下载无需从头开始；已完整的章节直接跳过
- 本地模式：管理多个本地目录，离线浏览已下载漫画（封面 / 名字 / 标签），点击打开所在文件夹
- 自动登录：配置保存账号密码后启动自动登录
- 多域名接口：内置接口域名列表，请求失败自动轮换到下一个可用域名，失效域名临时冷却跳过

## 技术栈

- .NET 10 / C#，纯 WPF（XAML），无 WebView2 依赖
- 核心库 `JmComic.Core`：API 客户端（签名 + AES-256-ECB 解密）、下载引擎、图片重组
- 测试 `JmComic.Core.Tests`：xUnit，覆盖解密、分块计算、文件名过滤、专辑构建

## 构建

```powershell
# 还原并构建
dotnet build JmComic.slnx

# 运行测试
dotnet test JmComic.slnx

# 发布单文件（win-x64，框架依赖）
.\publish.ps1
```

> 发布产物为框架依赖单文件（约 27MB），目标电脑需安装 .NET 10 Desktop Runtime（`winget install Microsoft.DotNet.DesktopRuntime.10`）。若需免安装的自包含包，可手动发布：`dotnet publish src/JmComic.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`（产物约 160MB）。

发布产物在 `publish/` 目录。

## 使用

1. （可选）点击「账号登录」登录，收藏夹功能需要登录
2. 「漫画搜索」输入关键词或 JM 号，点击漫画卡片进入「章节详情」
3. 勾选要下载的章节（支持左键拖动框选、Ctrl 多选、右键菜单），点击「下载勾选章节」
4. 下载完成后点击「打开下载目录」查看结果
5. 点击导航栏「本地」可查看历史下载的漫画，「管理路径…」可添加 / 移除扫描目录（默认包含下载目录）

## 数据目录

- 配置文件：`%APPDATA%\jmcomic-downloader\config.json`（与原 Tauri 版结构一致）
- 本地目录列表：配置中的 `localDirs` 字段（默认含下载目录）；每部漫画目录下的 `album.json` 保存标签 / 作者等元数据
- 默认下载目录：`%APPDATA%\jmcomic-downloader\漫画下载`

> 注意：配置文件中的用户名和密码为明文保存。
>
> 禁漫 API 域名失效时（登录/搜索报 404 等），程序会自动在接口域名列表中轮换并切换到可用域名
> （失效域名临时冷却跳过，避免每次请求都先等待超时）。可在 `config.json` 中添加 `"apiDomains"` 配置自己的域名列表
> （旧版单域名配置 `"apiDomain"` 仍兼容，优先读取 `apiDomains`），无需重新编译：
>
> ```json
> "apiDomains": ["www.cdngwc.cc", "www.cdngwc.net"]
> ```
>
> 本地模式中文名：扫描时会先从目录名提取已有的中文片段作为「中文名」（离线、零配置）。对纯外文标题，可在 `config.json` 中添加 `titleTranslate` 调用 OpenAI 兼容翻译接口（OpenAI / DeepSeek / 通义等），扫描后自动为缺失中文名的漫画补齐翻译，结果缓存在 `local-library-cache.json` 并写回 `album.json`：
>
> ```json
> "titleTranslate": {
>   "enabled": true,
>   "baseUrl": "https://api.deepseek.com/v1",
>   "apiKey": "sk-xxx",
>   "model": "deepseek-chat"
> }
> ```

## 免责声明

本工具仅作学习、研究、交流使用，使用本工具的用户应自行承担风险。




