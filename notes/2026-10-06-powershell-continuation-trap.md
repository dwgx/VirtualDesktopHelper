# PowerShell 续行陷阱：换行后以 `+` 开头会被当成加法（2026-10-06 复踩）

## 现象

往 `tools/export-docs-site.ps1` 里加一段文案，本意是把一个长字符串拆成几行拼接：

```powershell
  $gapNote = '<p class="note">……现在每次体检跑的是 <strong>'
    + $implemented + ' 项</strong>，上面这 ' + $checkTotal + ' 条是最初的清单。'
```

解析直接失败，而且报的不是"语法错误"，是：

```
Cannot convert value "项</strong>，上面这" to type "System.Int32".
+     + $implemented + ' 项</strong>，上面这 ' + $checkTotal + ' 条是最初的清单。'
```

## 原因

**续行如果以空白 + `+` 开头，PowerShell 5.1 把它当成算术加号**，于是
`$implemented + ' 项…'` 被求值为 `[int] + [string]`——先把字符串转 int，转不动就报错。

`$implemented` 是 35，本来是能转的；真正触发的是后面那段以数字开头的中文片段。

## 规矩（别再凭直觉写）

1. **续行不要以 `+` 开头。** 要拆就写成赋值累加：
   ```powershell
   $s = '第一段'
   $s = $s + '第二段'
   ```
2. **`.ps1` 必须是带 BOM 的 UTF-8**，否则 PS 5.1 按 ANSI 读，中文报错信息本身就会解析失败。
3. 改完立刻验：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\xxx.ps1`
   看它**输出**，不要只看退出码。

## 附带：这次的诊断弯路

第一版改法在同一个字符串里混用了两种续行风格，报错位置指向的那一行**看起来完全正常**。
我花了三轮在数引号、数括号、逐行二分解析，最后是把它整体重写成单行才过。

**教训：这种"报错行看起来没问题"的，改结构比继续查更快。**