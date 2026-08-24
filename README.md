# AI 软件商店

Windows 客户端：`C# .NET Framework 4.7.2 + WinForms`

服务端：`Go + PostgreSQL + nginx X-Accel-Redirect`

## 项目结构

- `src/AIShop.Client`：客户端主程序
- `src/AIShop.Updater`：独立客户端更新器
- `src/AIShop.Shared`：客户端共享模型和 changelog 解析
- `server`：Go 服务端
- `Docs/设计方案.md`：产品和协议设计

## 构建

```powershell
dotnet build .\AIShop.slnx
cd server
go build ./...
```

## 客户端 Release 产物

客户端和更新器的最终产物统一输出到仓库根目录的 `release` 文件夹：

```powershell
.\build-release.ps1
```

输出结果至少包含：

- `release\AI软件商店.exe`
- `release\AI软件商店.Updater.exe`

## 服务端环境变量

- `AISHOP_ADDR`：监听地址，默认 `:8080`
- `AISHOP_DATABASE_URL`：PostgreSQL 连接串
- `AISHOP_DATA_DIR`：投稿包保存目录，默认 `./data`
- `AISHOP_ACCEL_PREFIX`：nginx internal 路径前缀，默认 `/internal-packages/`
- `AISHOP_CLIENT_VERSION`：客户端最新版本号
- `AISHOP_CLIENT_UPDATE_URL`：客户端更新包下载地址
- `AISHOP_CLIENT_UPDATE_SHA256`：客户端更新包 sha256
- `AISHOP_CLIENT_CHANGELOG`：客户端更新日志
- `AISHOP_DEV_SERVE_FILES=1`：开发环境允许 Go 直接输出文件，生产不要启用

## nginx 示例

```nginx
location /internal-packages/ {
    internal;
    alias /data/aishop/packages/;
}

location /api/ {
    proxy_pass http://127.0.0.1:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
}
```

生产环境需要保证 `AISHOP_DATA_DIR/packages` 与 nginx `alias` 指向同一目录。
