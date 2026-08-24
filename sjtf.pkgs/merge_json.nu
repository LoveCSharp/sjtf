# 获取当前脚本完整路径
const script_path = path self
# 提取脚本所在文件夹（去掉脚本文件名）转为绝对路径
let script_dir = ($script_path | path dirname | path expand)
# 获取.net 解决方案路径
let sln_dir = ($script_dir | path dirname | path expand)

ls $"($script_dir)/pkg‑fragments"
| where { |row| ($row.name | str ends-with ".json") and ($row.name != "pkgs.json") }
| sort-by name -r
| each { open $in.name } | reduce { |acc, next| $acc | merge $next }
| to json | save --force $"($sln_dir)/sjtf.cli/data/pkgs.json"
