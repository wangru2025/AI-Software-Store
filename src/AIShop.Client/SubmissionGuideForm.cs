using System.Windows.Forms;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class SubmissionGuideForm : Form
    {
        public SubmissionGuideForm()
        {
            Text = "投稿说明";
            Width = 760;
            Height = 520;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Text =
                    "AI 软件商店投稿说明\r\n" +
                    "\r\n" +
                    "一、投稿包结构\r\n" +
                    "投稿文件必须是 zip。zip 根目录必须包含：\r\n" +
                    "- aishop.json\r\n" +
                    "- install.ps1\r\n" +
                    "- CHANGELOG.txt\r\n" +
                    "这些必需文件必须直接位于压缩包根目录，不能放在二级文件夹里。例如打开 zip 后应直接看到 aishop.json，而不是先看到某个目录。\r\n" +
                    "\r\n" +
                    "可选文件：\r\n" +
                    "- uninstall.ps1\r\n" +
                    "- update.ps1\r\n" +
                    "- README.txt / README.md\r\n" +
                    "- 其它安装所需文件\r\n" +
                    "\r\n" +
                    "由于服务器带宽有限，建议把较大的安装程序上传到 GitCode 或其它可靠平台，投稿 zip 内只存放 aishop.json、CHANGELOG.txt 和部署脚本；install.ps1 在执行部署流程时自行下载并安装真实程序包。\r\n" +
                    "\r\n" +
                    "二、aishop.json 字段\r\n" +
                    "示例：\r\n" +
                    "{\r\n" +
                    "  \"id\": \"author.software-name\",\r\n" +
                    "  \"name\": \"软件名称\",\r\n" +
                    "  \"version\": \"1.0.0\",\r\n" +
                    "  \"author\": \"作者名\",\r\n" +
                    "  \"summary\": \"一句话简介\",\r\n" +
                    "  \"requiresAdmin\": false,\r\n" +
                    "  \"install\": \"install.ps1\",\r\n" +
                    "  \"uninstall\": \"uninstall.ps1\",\r\n" +
                    "  \"update\": \"update.ps1\",\r\n" +
                    "  \"updateMode\": \"script\"\r\n" +
                    "}\r\n" +
                    "\r\n" +
                    "字段说明：\r\n" +
                    "- id：必填，软件唯一标识，只能新版本沿用，不能改成另一个软件。\r\n" +
                    "- name：必填，软件名称。上传后仍可在投稿管理中编辑。\r\n" +
                    "- version：必填，当前投稿版本。已上传版本不能原地修改，修改安装内容必须发新版本。\r\n" +
                    "- author：建议填写，显示用作者名。\r\n" +
                    "- summary：必填，软件简介。上传后仍可在投稿管理中编辑。\r\n" +
                    "- requiresAdmin：是否需要管理员权限。需要写 true 或 false。\r\n" +
                    "- install：安装脚本路径，默认 install.ps1。\r\n" +
                    "- uninstall：可选，卸载脚本路径。声明了就必须存在。\r\n" +
                    "- update：可选，更新脚本路径。声明了就必须存在。\r\n" +
                    "- updateMode：可选，script / install-over / clean-install / manual。\r\n" +
                    "\r\n" +
                    "三、CHANGELOG.txt 格式\r\n" +
                    "必须按版本块书写，当前投稿版本必须有对应块：\r\n" +
                    "=== 1.2.2.2 | 2026-08-18 ===\r\n" +
                    "- 修复安装失败时的清理问题\r\n" +
                    "- 优化下载中断后的恢复\r\n" +
                    "\r\n" +
                    "=== 1.1.1.1 | 2026-08-01 ===\r\n" +
                    "- 首次发布\r\n" +
                    "\r\n" +
                    "客户端会按版本块生成更新日志列表。回车打开某个版本时，只显示该版本对应内容。\r\n" +
                    "\r\n" +
                    "四、安装脚本必须使用的函数\r\n" +
                    "客户端执行脚本前会注入 AIShop.Package.psm1，脚本可直接调用这些函数：\r\n" +
                    "\r\n" +
                    "Set-AIShopStatus \"正在准备安装\"\r\n" +
                    "用途：上报当前状态文本。\r\n" +
                    "\r\n" +
                    "Set-AIShopProgress -Percent 30 -Message \"正在复制文件\"\r\n" +
                    "用途：上报进度，Percent 范围 0 到 100。\r\n" +
                    "\r\n" +
                    "Register-AIShopUninstall -Command \"C:\\Program Files\\xxx\\uninstall.exe\" -Arguments \"/S\"\r\n" +
                    "用途：登记卸载命令。客户端之后会优先使用这个命令卸载。\r\n" +
                    "\r\n" +
                    "Register-AIShopInstallLocation \"C:\\Program Files\\xxx\"\r\n" +
                    "用途：登记安装目录。\r\n" +
                    "\r\n" +
                    "Test-AIShopCancel\r\n" +
                    "用途：检查用户是否点了取消。长耗时步骤前后都应该调用。\r\n" +
                    "\r\n" +
                    "Complete-AIShopInstall\r\n" +
                    "用途：通知客户端安装完成。\r\n" +
                    "\r\n" +
                    "Fail-AIShopInstall \"安装失败，请稍后重试\"\r\n" +
                    "用途：通知客户端安装失败，并结束脚本。\r\n" +
                    "\r\n" +
                    "五、install.ps1 示例\r\n" +
                    "Set-AIShopStatus \"正在准备安装\"\r\n" +
                    "Test-AIShopCancel\r\n" +
                    "$target = Join-Path $env:LOCALAPPDATA \"DemoApp\"\r\n" +
                    "New-Item -ItemType Directory -Force -Path $target | Out-Null\r\n" +
                    "Set-AIShopProgress -Percent 30 -Message \"正在复制文件\"\r\n" +
                    "Copy-Item -Path \".\\app\\*\" -Destination $target -Recurse -Force\r\n" +
                    "Register-AIShopInstallLocation $target\r\n" +
                    "Register-AIShopUninstall -Command \"powershell.exe\" -Arguments \"-ExecutionPolicy Bypass -File `\"$target\\uninstall.ps1`\"\"\r\n" +
                    "Set-AIShopProgress -Percent 90 -Message \"正在完成设置\"\r\n" +
                    "Complete-AIShopInstall\r\n" +
                    "\r\n" +
                    "六、上传和编辑规则\r\n" +
                    "- 上传校验通过后状态为草稿。\r\n" +
                    "- 草稿不会出现在商店主列表，需要投稿者手动上架。\r\n" +
                    "- 已有版本只能编辑软件名称和简介。\r\n" +
                    "- 安装包、脚本、版本号、hash、更新日志不能原地修改，必须发新版本。\r\n" +
                    "- zip 不能包含 ../ 或绝对路径。\r\n" +
                    "- 脚本输出的普通文本会写入日志，用户界面只显示上述函数上报的信息。\r\n"
            };
            Controls.Add(text);
        }
    }
}
