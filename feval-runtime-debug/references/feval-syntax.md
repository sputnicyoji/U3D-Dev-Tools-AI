# Feval 表达式语法详细参考

> 基于 `com.tfw.feval@1.3.1`（`Feval.Core.dll`）反编译 + 对运行 Service 的实测探针归纳。CHANGELOG 与 README 与实际行为存在多处出入，遇分歧**以本文档为准**。

## 按症状速查

带着 feval 抛出的报错或观察到的"看似成功但行为不对"先来这里查；命中后再跳到对应章节读细节与 workaround。**没命中**才走 [目录](#目录) 按特性翻。

| 看到这个 | 跳去 | 找子节 |
|---|---|---|
| `Illegal character: ;` / `#` / `{` / `}` / `!` / `~` / `?` / `&` | [词法（Lexer）](#词法lexer) | — |
| `Unexpected token (` （想用括号分组） | [明确不支持的语法（全清单）](#明确不支持的语法全清单) | 括号分组 |
| `Syntax Error` 出现在 `>` / lambda / `x => ...` / `arr<int>` | [明确不支持的语法（全清单）](#明确不支持的语法全清单) | — |
| `Symbol 'X' not found`（缺命名空间或未 using） | [标识符解析顺序](#标识符解析顺序) | — |
| `Member 'X' not found` 而 `X` **是 static** | [Member 访问语义（含静态成员坑）](#member-访问语义含静态成员坑) | 静态成员经实例链查不到 |
| `Member 'X' not found` 而 `X` **是方法**（写漏了 `()`） | [方法 / 构造器调用](#方法--构造器调用) | 方法不是一等公民 |
| `Method 'X' not found` / `does not overload`（参数类型对不上） | [方法 / 构造器调用](#方法--构造器调用) | 重载选择 |
| `Constructor not found` / `new T[N]` 直接 NRE | [方法 / 构造器调用](#方法--构造器调用) | 构造器 |
| `==` 表达式只返回左操作数（看似没比较） | [运算符全表](#运算符全表) | 比较运算 |
| `arr[k] = v` / `dict["k"] = v` 看似成功但容器没变 | [赋值语义（含索引器赋值的悄默失败）](#赋值语义含索引器赋值的悄默失败) | — |
| `long_field.Equals(intLiteral)` 永远返回 False | [方法 / 构造器调用](#方法--构造器调用) | 数值字面量与 .Equals() 的悄默 False 陷阱 |
| `null + "str"` / `"str" + null` 抛 `ArgumentNullException` | [运算符全表](#运算符全表) | 二元运算的 null 行为 |
| `Cannot apply bitwise or operation on different types` | [运算符全表](#运算符全表) | \| 的底层类型限制 |
| `Operator or not supported for type X` | [运算符全表](#运算符全表) | \| 的底层类型限制 / 非数值类型 fall back |
| `OverflowException` 出现在算术（如 `Convert.ToUInt32(-1)`） | [运算符全表](#运算符全表) | 数值类型晋升表 |
| `out var v` 报 `Syntax Error` 或 v 没出现 | [out 参数](#out-参数) | — |
| `(Pending)` 长时间不更新（异步任务） | [运行时悄默失败（看似成功的陷阱）](#运行时悄默失败看似成功的陷阱) | — |
| 字符串里 `\n` `\t` 没转义 / 含真换行报 `Syntax Error` | [字符串内插](#字符串内插) | — |
| 仅在 IL2CPP 真机出现的 `Method/Member not found` | [Reflector 自定义钩子与 IL2CPP](#reflector-自定义钩子与-il2cpp) | — |
| 同名 type 跨 dll，命中"昨天好的命令今天调到别处" | [方法 / 构造器调用](#方法--构造器调用) | 泛型静态方法的"最后一个 type 赢"非确定 |
| 文档里没覆盖、Top 表也没命中 | [源码定位与反编译](#源码定位与反编译) | 直接读 / 反编译 feval 源 |

## 目录

1. [运行模式与上下文](#运行模式与上下文)
2. [词法（Lexer）](#词法lexer)
3. [字面量](#字面量)
4. [关键字](#关键字)
5. [运算符全表](#运算符全表)
6. [一元 / 后缀运算](#一元--后缀运算)
7. [标识符解析顺序](#标识符解析顺序)
8. [Member 访问语义（含静态成员坑）](#member-访问语义含静态成员坑)
9. [方法 / 构造器调用](#方法--构造器调用)
10. [扩展方法](#扩展方法)
11. [out 参数](#out-参数)
12. [赋值语义（含索引器赋值的悄默失败）](#赋值语义含索引器赋值的悄默失败)
13. [索引访问](#索引访问)
14. [typeof](#typeof)
15. [using 指令](#using-指令)
16. [字符串内插](#字符串内插)
17. [变量与 ans](#变量与-ans)
18. [内置函数全表](#内置函数全表)
19. [明确不支持的语法（全清单）](#明确不支持的语法全清单)
20. [运行时悄默失败（看似成功的陷阱）](#运行时悄默失败看似成功的陷阱)
21. [Reflector 自定义钩子与 IL2CPP](#reflector-自定义钩子与-il2cpp)
22. [包装脚本特有约定](#包装脚本特有约定)
23. [Service 网络协议](#service-网络协议)
24. [源码定位与反编译](#源码定位与反编译)

---

## 运行模式与上下文

| 模式 | 入口 | 默认 using | 是否远程 |
|---|---|---|---|
| **Standalone Editor** | Unity `Window/Feval` 菜单 | `UnityEngine`、`UnityEditor`、`System` | 否，跑在 Editor AppDomain |
| **Service**（本 skill 用的就是这个） | `EvaluationService.Run(9999)` 在游戏内启动；`feval <ip>:9999` 连接 | **空，零 using** | 是 |

> **重要**：连接到 Service 时 `usings()` 输出**空列表**。**所有类型必须用完全限定名**（`UnityEngine.Vector3`，不是 `Vector3`），除非你先发一条 `using UnityEngine` 把命名空间加进 Context。

**Context 是 process 级单例**（`Context.Main`）：
- 你声明的 `var x = ...`、发出的 `using ...`、`ans` 的最近值，**会跨调用、跨连接持续存在**，直到游戏进程重启。
- 多个并发会话共享同一 Context，互相会污染。如果不希望影响他人，避免用 `var` 创建变量；改用一次性表达式或带前缀的临时名（`var __probe1 = ...`）。

**所有 `Evaluate` 在 Unity 主线程执行**：Service 用 `Update()` 队列分发请求。这意味着探测是**无锁安全**的（不会和游戏逻辑并发），但耗时操作会卡帧。

---

## 词法（Lexer）

来源：`Feval.Syntax/Lexer.cs`。

### 字符级识别

| 字符 | 处理 |
|---|---|
| `(` `)` `<` `>` `[` `]` | 括号类 |
| `+` `-` `*` `/` | 算术 |
| `,` `.` `=` (`==` 双字符) | 分隔/赋值/相等 |
| `$` | 字符串内插起始 |
| `` ` `` | dump 操作符 |
| `\|` | 位或 |
| `"` | 字符串字面量起始 |
| `0`-`9` | 数字字面量 |
| `_` 或字母 | 标识符 / 关键字 |
| 其他任何字符 | **抛 `Illegal character: X`**（含 `;` `!` `?` `&` `~` `^` `:` `#` `{` `}` 等） |

### 不识别的常见字符与典型错误

| 你可能想写 | 错误信息 | 替代 |
|---|---|---|
| `;` | `Illegal character: ;` | 拆成多行（多个 `--command` 或 `--commands-file` 多行） |
| `!` `~` | `Illegal character: !` / `~` | 没有逻辑/位非；改写表达式 |
| `?` | `Illegal character: ?` | 没有 `?:` `??` `?.` |
| `&` | `Illegal character: &` | **没有位与 `&`** —— CHANGELOG 1.3.0 写"增加按位与"实际未实现 |
| `^` | `Illegal character: ^` | 没有 XOR |
| `#` | `Illegal character: #` | feval 本身**不**把 `#` 当注释；只有本 skill 的 `--commands-file` 包装层会跳过 `#` 行 |
| `{` `}` 字面 | `Illegal character: {` | 不能 `new int[]{1,2,3}`、不能在内插里写 `{{` 转义 |
| `\` | （在字符串内是字面量字符）| 字符串**不识别 `\n` `\t` `\\` 等转义**，详见下方 |

### 注释

feval 表达式语法**没有注释**。本 skill 的 `--commands-file` 模式中 `#` 开头是**包装脚本**层面的注释（见 [包装脚本特有约定](#包装脚本特有约定)）。

---

## 字面量

| 字面量 | 源码归类 | 实际类型 |
|---|---|---|
| `123` | `IntLiteral` (int.TryParse 优先) | `System.Int32` |
| `12345678901` | `LongLiteral`（int 装不下时） | `System.Int64` |
| `1.5` | `FloatLiteral` (float.TryParse) | **`System.Single`（不是 double）** |
| `1.5f` | 实际只识别 `1.5`，结尾的 `f` 被当作下一 token；解析在二元运算入口处停止 → 返回 1.5 | 等价于 `1.5`，**不要依赖 `f` 后缀**，没有任何作用 |
| `1.5d` / `1.5m` | 同上，不被识别 | 不要写 |
| `"abc"` | `StringLiteral` | `System.String` |
| `true` / `false` | 关键字 | `bool` |
| `null` | 关键字 | `null` |

### 字符串字面量限制

- **不识别反斜杠转义**：`"a\nb"` 不是含换行的 6 字符串，而是 6 个字面字符 `a`、`\`、`n`、`b`、`...`。Lexer 直接 `StringBuilder.Append(Current)`。
- **真实换行/回车导致 `Syntax Error: illegal string`**：字符串里不能有真换行字符。
- **唯一的"转义"**：双引号 `""` 表示一个嵌入的双引号（来自 lexer 中 `Peek(1) == '"'` 的特例）。
- 想塞控制字符 → 用 `((char)10).ToString()` 拼接（`+` 串接），或在工程里加常量并用 FQN 读出来。

### 数字边界与 `1.foo` 这类奇怪写法

`ReadNumber` 的循环条件是 `char.IsDigit(Current) || (Current == '.' && !flag)`：

| 写法 | Lexer 切分 | 行为 |
|---|---|---|
| `1.5` | 单个 `FloatLiteral 1.5` | 浮点字面量 |
| `1.` | `FloatLiteral 1.0`（`float.TryParse("1.")` = 1.0） | 形如 `1.` 单独写的，等价 `1.0f` |
| `1.foo` | `FloatLiteral 1.0` + `.` + `IdentifierToken foo` | **`(1.0f).foo` —— 等价对 float 调成员**，并不是预期的 `1.0` 后跟 `.foo` 错语法。访问 float 类型上的 `foo` → `Member 'foo' not found` |
| `1..ToString()` | `FloatLiteral 1.0` + `.` + `IdentifierToken ToString` + `()` | `(1.0f).ToString()` → `"1"`，可用作"避免变量"的一行表达式 |
| `42a` | `IntLiteral 42` + `IdentifierToken a` | 数字消耗到第一个非数字位置；后面 `a` 当下一个 token 解析（`Symbol 'a' not found`） |
| `0123` | `IntLiteral 123`（`int.TryParse` 不识 `0o/0x/0b` 进制） | 没有 8 进制 / 16 进制字面量 |
| `1e3` `1.5e2` `0x1F` | `IntLiteral 1` + `IdentifierToken e3` 等 | 没有科学计数法、没有 16 进制；要 `1000` 就字面写 `1000` |

---

## 关键字

来自 `SyntaxDefinition.m_Keywords`：

`new` / `true` / `false` / `null` / `typeof` / `var` / `using` / `out`

来自 `SyntaxDefinition.m_TypeKeywords`（仅这 5 个）：

`string` / `int` / `long` / `float` / `double`

> **注意**：`bool`、`byte`、`short`、`char`、`object`、`decimal`、`uint`、`ulong` **不是关键字**。`typeof(bool)` 报 `Sequence contains no elements`；写 `typeof(System.Boolean)`。

`is`、`as`、`nameof`、`if`、`for`、`while`、`return`、`break`、`continue`、`switch`、`case`、`try`、`catch` 等控制流 / 转换关键字**全不存在**——它们会被当作普通 IdentifierToken，要么解析失败要么和 `==` 一样被悄悄丢弃。

---

## 运算符全表

来自 `SyntaxDefinition.m_BinaryOperatorPriorities` + `Evaluator.EvaluateBinaryExpression` + `ReflectionUtilities`。

### 已实现的二元运算（仅 5 个）

| 运算符 | priority | 行为 | 注 |
|---|---|---|---|
| `.` | 6 | 成员访问 | 由 Postfix 阶段处理，但也注册为 binary |
| `*` `/` | 5 | 数值乘除（11 种数值类型）；fall back 到 `op_Multiply` / `op_Division` | |
| `+` | 4 | 数值加；**任一边为 `string` → 字符串拼接（自动 ToString）**；fall back 到 `op_Addition` | |
| `-` | 4 | 数值减；fall back 到 `op_Subtraction` | |
| `\|` | 3 | 整型/枚举的位或；fall back 到 `op_BitwiseOr` | 仅 byte/int/long 三种 underlying 类型，详见下表 |

### 数值类型晋升表（`+ - * /` 用）

`ReflectionUtilities.m_NumericTypes` 中的"优先级"按数组下标定义（小→大）：

```
byte → sbyte → short → ushort → int → uint → long → ulong → float → double → decimal
```

`a + b` 的结果类型 = `m_NumericTypes` 索引更大的那个；两边都被 `Convert.ToXxx` 升到该类型再做原生 `+ - * /`。

**易踩的坑：**

| 表达式 | 实际行为 |
|---|---|
| `1 + 1.5` | int(下标 4) vs float(下标 8) → 升到 float → `2.5f` |
| `1 + 1.5 + System.Convert.ToDouble(0)` | 表达式里出现 double 时全部升到 double。**feval 没有 `0.0d` 字面量、也没有 C 风格 `(double)` 强转**，要拿 double 只能 `System.Convert.ToDouble(x)` / `System.Double.Parse("0")` |
| `System.Convert.ToInt32(-1) + System.Convert.ToUInt32(1)` | int 下标 4 vs uint 下标 5 → 升到 uint → `Convert.ToUInt32(-1)` **抛 OverflowException** |
| `System.Convert.ToUInt64(1) + 1.5` | ulong 下标 7 → float 下标 8 → 升到 float，**ulong 转 float 损失精度**（21+ 位整数会舍入） |
| `System.Convert.ToDecimal(1) + 0.5` | 升到 decimal → `Convert.ToDecimal(0.5f)` → 正常 |
| `System.Convert.ToByte(1) + System.Convert.ToByte(2)` | byte → byte → C# `byte+byte` 隐式返回 int，结果 `int 3`（CLR 行为） |

> **没有 C 风格强转**：feval 完全不识别 `(int)x`、`(double)x`、`(SomeType)x` 这类语法（`(` 在 term 入口处不被允许）。要换类型只能调 `System.Convert.ToXxx(...)` 或类型自带的 `.ToInt32()` / `.ToString()` 等方法。

**非数值 / 类型不在 11 种中**：fall back 到 `Operator(opName, a, b)`，反射查 `a.GetType().GetMethod(opName, Static|Public, null, new Type[]{a.GetType(), b.GetType()}, null)`。**精确匹配**两个参数类型，不做隐式宽化；`string + Vector3` 找不到 `op_Addition(String, Vector3)` 会抛 `does not overload`。所以基本只能给"两边同类型且类型自己定义了 `op_Addition`"的场景用。

#### 二元运算的 null 行为：`+ - * / |` 全部对 null 抛 ArgumentNullException

`OperatorAdd` / `OperatorSubtraction` / `OperatorMultiply` / `OperatorDivision` 入口都是 `ThrowIfArgumentIsNull(a, b)`，**先于** "string 拼接"或"数值"分支触发：

```text
"player=" + null                  # ❌ ArgumentNullException
null + "x"                        # ❌ ArgumentNullException
"a" + ((string)null)              # ❌ 同上（强转语法不存在不重要——核心是值是 null）
```

**这就是 feval 拼日志最容易踩的坑**：你以为 `"errMsg=" + ack.errMsg` 在 errMsg 为 null 时会拼出 `"errMsg="`，实际直接异常。

**安全替代**（按推荐度排）：
1. **字符串内插**：`$"errMsg={ack.errMsg}"` → 走 `StringBuilder.Append(obj)`，对 null 写空串，**不抛**。
2. **`Object.ToString()` 派生**：先 `var s = ack.errMsg`，再 `s == null ? ... : ...`——但 feval 没三元，只能再用 `String.Concat`：
3. **`String.Concat(...)` 静态方法**：`System.String.Concat("errMsg=", ack.errMsg)` 对 null 写空串，安全。
4. 用扩展方法路径强转（实例链）：`ack.errMsg.ToString()` —— 但**对 null 实例调方法会 NRE**，所以这条只能用在确认非 null 时。

**结论**：拼字符串可能含 null 字段时，**永远首选 `$"..."` 字符串内插**，不要用 `+`。

### `|` 的底层类型限制

`ReflectionUtilities.BitwiseOr` 仅原生支持 3 种 underlying 类型：

| 类型 | 支持 | 说明 |
|---|---|---|
| `int` | ✅ | 原生 `(int) \| (int)` |
| `long` | ✅ | 原生 `(long) \| (long)` |
| `byte` | ✅ | 原生 `(byte) \| (byte)`（结果是 int，但 underlying 是 byte 的 enum 会 `Enum.ToObject` 包回来） |
| `short` `ushort` `uint` `ulong` `sbyte` `float` `double` `decimal` | ❌ | 抛 `Operator or not supported for type Xxx` |

**枚举特殊**：左右两边类型必须**完全相同**（`type != type2` 直接抛 `Cannot apply bitwise or operation on different types`）。所以两个不同 enum 类型的 flags 不能 `|`，要先统一类型。

非原生类型走 `op_BitwiseOr` 反射，同样要求两边类型完全一致，且类型必须重载该运算符。

### 比较运算（**全无**——重申）

`==` `<` `>` 在 lexer 里识别为 token 但 `m_BinaryOperatorPriorities` 没列 → `GetBinaryOperatorPriority` 返回 0 → 二元解析器循环立即 break → **整条表达式只取左操作数，等号右边被默默丢弃**：

```text
1 == 2          => 1        # 不是 False！
"a" == "a"      => "a"
true == false   => True     # LHS 的 ToString
```

> **统一根因（一类悄默失败的源头）**：`SyntaxTree.Parse` 走 `ParseBinaryExpression()` 作根，循环条件是「下一个 token 在 priority 表里且 priority ≥ parent」。**只要循环 break，剩余的 token 就停留在 `Tokens` 数组里、不再进入 AST，运行期完全看不到**。这套机制把 `==`、`<`、`>`（以及当成 IdentifierToken 的 `is`/`as`/`await` 等）后面的内容统一吞掉——你看到「LHS 被原封返回」时，根因都是这条。
>
> **永远不要在 feval 里写比较运算符。要判断相等只能：**

```text
# 用 Object.Equals
1.Equals(2)
"a".Equals("b")
# 或用类型自己的方法
SomeNs.Foo.IsEmpty()
# 或先取值，把判等留给上层（脚本/agent）人眼对比
```

### 词法识别但**完全未接 priority** 的 token

`==`（`EqualsEqualsToken`，但未列入 priority 表 → 见下方"比较运算"小节）、`<` `>`（`OpenAngleBracketToken` / `CloseAngleBracketToken`，仅供泛型参数列表用，不是比较）。`!=` `<=` `>=` `&`、`&&` `||` 等连 token 都没有。

---

## 一元 / 后缀运算

### 一元前缀（仅 2 个）

| 写法 | 行为 |
|---|---|
| `-x` | 数值取负（int/long/float/double 直接，其他类型走 `op_UnaryNegation`） |
| `` `x `` | 等价 `dump(x)`，调用注册的 dumper 把对象序列化 |

**没有** `+x`（一元正号）、`!x`、`~x`。

### 后缀链（在 Postfix 阶段循环处理）

| 后缀 | 含义 |
|---|---|
| `(args)` | 调用 |
| `<T1, T2>(args)` | 泛型调用 |
| `[k]` | 索引访问 |
| `.name` | 成员访问 |
| `= expr` | 赋值（左侧必须是 IdentifierName 或 MemberAccess 或 Declaration） |

### **不存在**的语法分组

```text
(1 + 2)          → Syntax Error: Unexpected token '('
-(1 + 2)         → 同样错（一元 `-` 后必须是 Term，而 `(` 不是 Term 入口）
(int)1.5         → 同样错（不支持 C 风格强转）
(x as int)       → 同样错（双重失败：`(` + `as`）
```

**`(` 仅作为参数列表起始**（在 InvocationExpression 里）。**没有括号分组**——任何需要分组的运算改用临时变量：

```text
# 想算 (a + b) * c
var t = a + b
t * c
```

---

## 标识符解析顺序

`Evaluator.EvaluateIdentifierNameExpression`：当解析器遇到一个 `IdentifierToken`（即写一个裸名字 `Foo`）时按以下顺序解析：

1. **Context 已有 Symbol** → `m_Context.GetSymbol(name)`
   - `VariableSymbol`：返回它的 `Value`，并把 TypeOrNamespace 设为 `value.GetType()` 的 namespace + 该单一 Type。
   - 内置 `FunctionSymbol`（`help` `dump` `usings` `vars` `assemblies` `version` `copyright`）：**直接返回 Symbol 对象本身**——单独写 `help` 而不是 `help()` 会拿到 FunctionSymbol 的 ToString，**不会自动调用**。
2. **TryLookupTypeOrNamespace**：依次拼接每个 using 命名空间 + 当前名字
   - `using` + `.` + `name` 的全限定名 `LookupTypes` → 找到至少一个 Type 就返回。
   - 同一 `name` 在不同 using 下都能匹配会返回**第一个命中的 using**——多个命名空间下同名类型会有先达者优先的非确定性。
   - 全限定名查不到 Type 时，再看是否构成 namespace（任意已加载 type 的 Namespace 字段包含该串）。
3. **裸 namespace / 裸全限定名**：上一步全失败后再退回 `m_Context.IsNamespace(text)` + `m_Context.LookupTypes(text)`——所以**直接写 `UnityEngine.Vector3.zero` 不需要先 `using UnityEngine`**。第一段 `UnityEngine` 会落到这一步认成 namespace，之后链式 MemberAccess 走"namespace.text" 拼接路径找 Type。
4. **以上全失败** → 抛 `Type or namespace <name> not found`。

### 名字解析的几个隐性事实

- **变量名"遮蔽"类型**：你 `var Vector3 = 1` 之后，`Vector3` 解析为 int 1，再 `Vector3.zero` 报 `Member 'zero' not found`。Context 是单例，**所有连接共享**——避免用项目类名作变量名。
- **变量值是 `null` 时丢失类型信息**：`var x = null; x.Foo` → 抛 **`Symbol 'x' not found`**（不是 NullReferenceException）。`EvaluateIdentifierName` 见 VariableSymbol.Value=null 跳过 `TypeOrNamespace` 设值，回到 `EvaluateMemberAccess` 时 `expression2.Value=null` 且 `TypeOrNamespace.IsEmpty()`，于是命中 `throw new Exception("Symbol '" + ... + "' not found")` 那一行。
- **`IsNamespace` 有副作用**：它走遍所有 imported assembly 的所有 Type 缓存命名空间。第一次调用慢；之后查缓存。**意味着裸写一个未知的"看起来像 namespace"的名字，会触发一次全 assembly Type 扫描。**
- **`IsNamespace` 用 `Contains`，不是 equality**：`m_VisitedNamespaces` 走完之后判断的是 `@namespace.Contains(name)`——**子串匹配**。所以 `IsNamespace("eng")` 在有 `UnityEngine` 命名空间时会返回 true。这是 feval 的 bug 但不致命，因为后续 `LookupTypes("eng")` 会失败 → 抛 `Type or namespace not found`。

---

## Member 访问语义（含静态成员坑）

`obj.Member` 路径来自 `Evaluator.EvaluateMemberAccessExpression` → `ReflectionUtilities.TryGetMemberValue` → `TypeExtensions.GetPropertyOrField`。

### 关键事实

`GetPropertyOrField` 选 BindingFlags 时是**二选一**：

```csharp
BindingFlags bindingFlags = ((instance != null) ? BindingFlags.Instance : BindingFlags.Static);
```

**不是** `Instance | Static`！这意味着：

| 写法 | 结果 |
|---|---|
| `Logic.LLogin.I.IsLogin` （IsLogin 是 instance prop） | ✅ 找到 |
| `Logic.LLogin.I.CharacterToken` （CharacterToken 是 static field） | ❌ `Member 'CharacterToken' not found`——因为通过实例链查只用 Instance flags |
| `Logic.LLogin.CharacterToken` | ✅ 找到（前缀解析为 Type，instance=null → 用 Static flags） |

**经验法则：**
- 不确定一个成员是 static 还是 instance 时，**先 grep 工程源码**（`grep -rn "static.*MemberName\b" Assets/`）；找不到源码时优先按**类名直访**试一次，失败再退回实例链。
- 对 `Mgr.I.Foo` 这种"单例 + 静态字段"组合的反模式（很常见），永远走 `Mgr.Foo`。

### 继承

Instance flags 用了 `BindingFlags.FlattenHierarchy`，并且**还会显式向 BaseType 走一层循环**（`GetPropertyOrField` 的 while）。所以基类 instance 字段能找到。Static 已通过 FlattenHierarchy 覆盖。

### 方法不是一等公民——`obj.Method` 不带括号会 `Member not found`

`Evaluator.EvaluateMemberAccessExpression` 拿到 `expression2.Value != null` 后，**只调 `ReflectionUtilities.TryGetMemberValue`**，而该路径下的 `TypeExtensions.GetPropertyOrField` 只查 `FieldInfo` / `PropertyInfo`——**完全不查 MethodInfo**。所以：

```text
Logic.LPlayer.I.GetPlayerInfo            # ❌ Member 'GetPlayerInfo' not found
Logic.LPlayer.I.GetPlayerInfo()          # ✅ 直接调用
var f = Logic.LPlayer.I.GetPlayerInfo    # ❌ 同样报 Member not found；feval 拿不到方法引用
```

**结论**：feval 没有"取方法引用"的能力。要复用一段方法链，**只能复制粘贴整条表达式**，或者 `var x = ThatMethod()` 把**结果**存下来。Lambda / Delegate / `nameof(Method)` / `MethodInfo` 拿引用都不行。**例外**：`Type.GetMethod("Name")` 反射拿到 MethodInfo 是可行的（因为这是属性而非方法引用），但代价是丢失重载选择。

---

## 方法 / 构造器调用

### 实例方法

`obj.Method(args)` → 用 `Instance` flags 在 `obj.GetType()` 上找方法。

- **`obj.StaticMethod(args)` 不行**（同上 binding flags 问题）。永远走 `Type.StaticMethod()`。
- **找不到时尝试扩展方法**：`ExtensionMethodCache.FindExtensionMethod`。要求扩展方法已在 imported assemblies 里。

### 静态方法

`Type.Method(args)` → 用 `Static` flags 在 Type 上找方法。

### 默认参数

`Invoke` 调用时 `completingDefaultArgs: true`：实参不够时用 `ParameterInfo.DefaultValue` 填充。所以 `obj.M()` 可以省略所有有默认值的参数。

### 重载选择

`MatchArgumentAndParameterTypes`：
- 实参数 ≤ 形参数；
- 每个实参类型必须 `IsAssignableFrom`（`null` 实参除外）；
- 实参短缺的尾部形参必须 `HasDefaultValue`。
- 选 `FirstOrDefault` —— **不做最佳匹配评分**。所以同名多重载里你没法精确控制走哪一个，可能命中"第一个能匹配的"。

### 泛型方法

`Type.GetGenericMethod(name, flags, genericArgs, argTypes)` → 找方法 → `MakeGenericMethod`。

```text
Some.Ns.Mgr.Get<Some.Ns.UIFoo>()        # 静态泛型
obj.Cast<int>(arg)                       # 实例泛型
```

> **泛型实参也要完全限定**：写 `Mgr.Get<Some.Ns.UIFoo>()`，不要写 `Mgr.Get<UIFoo>()`，除非已 `using Some.Ns`。

#### 泛型实参不能嵌套泛型 / 不能写数组类型

Parser 把每个泛型实参当作 `ParseBinaryExpression()` 来解析，而**任何带 `<...>` 的标识符在 postfix 阶段都会被强制识别成 `GenericInvocationExpression`，紧跟着必须出现 `(`**（见 `Parser.ParsePostfixExpression` 的 `OpenAngleBracketToken` case）。所以——

```text
Mgr.Get<System.Collections.Generic.List<int>>()      # ❌ Syntax Error
                                                     #    内层 List<int> 找不到 ( 报错
Mgr.Get<int[]>()                                     # ❌ Syntax Error
                                                     #    [] 进 IndexAccess 但 [ 后立即 ] 没有 key
```

**workaround**（按性价比排序）：
1. 在工程里加一个具体化的非泛型入口：`public static List<int> GetIntList() => Get<List<int>>();`，然后 feval 直接调 `Mgr.GetIntList()`。
2. 用反射手动构造（啰嗦但能写）：
   ```text
   var t = typeof(System.Collections.Generic.List<int>)
   var m = Some.Ns.Mgr.GetType().GetMethod("Get").MakeGenericMethod(t)
   m.Invoke(null, null)
   ```
3. 改用反射 `Type.MakeGenericType(...)` 自己拼。

#### 泛型静态方法的"最后一个 type 赢"非确定

`EvaluateGenericInvocationExpression` 静态分支的 `foreach` 遍历 `typeOrNamespace.Types` **没有 `break`**：

```csharp
foreach (Type type in typeOrNamespace.Types) {
    methodInfo = type.GetGenericMethod(...);    // 每轮覆盖
    expression.TypeOrNamespace = typeOrNamespace;
}
```

意味着同名 type 跨 assembly 出现时（HybridCLR / 多套 hotfix dll 加载并存的场景常见），**LookupTypes 返回的列表里最后一个 type 的方法**会胜出。如果你看到"昨天好的命令今天调到了别的 dll 里的同名类"，先怀疑这条。**workaround**：用 FQN 写明 namespace 缩小匹配；或者改走实例链（实例链只取 `value.GetType()` 一个 type，没有歧义）。

### 构造器

`new Type(args)` / `new Type<T>(args)`：

- 走 `Type.GetConstructor(argTypes)`——**精确匹配 argTypes**。
- 实参不足 → 补 `Type.Missing` 喂给 `Invoke`（让 .NET 用默认值），可被 `HasDefaultValue` 的尾参吸收。
- 实参超量 → `GetConstructor` 找不到精确签名 → 抛 **`Constructor not found`**。
- **`new int[3]`、`new int[]{1,2,3}` 都不正确支持**：前者被解析为 `new` + IndexAccessExpression（`MethodExpression as InvocationExpressionSyntax` 拿到 null），运行时直接 NRE；后者直接卡在 lexer 的 `{`。要建数组用 `System.Array.CreateInstance(typeof(int), 3)` 然后 `arr.SetValue(value, i)` 写入。

### 重载选择（含历史 bug）

`TypeExtensions.GetMethods` 用 `generic == method.ContainsGenericParameters` 把泛型/非泛型分流，再走 `MatchArgumentAndParameterTypes` 过滤、最后 `FirstOrDefault`：

- **同一桶里多重载**——返回**枚举顺序里第一个匹配的**，不做最佳匹配评分。`m(int)` 与 `m(long)` 都能吃 int 参数时，谁排前面谁赢，受运行时 reflection 顺序影响，**理论上不稳定**（实际通常稳定但不可依赖）。
- **CHANGELOG 1.0.1 历史 bug**：同名同签名的泛型与非泛型方法曾在 1.3.x 之前的版本里互相串扰。当前 1.3.1 已通过 `generic == method.ContainsGenericParameters` 分流；若仍命中异常，先确认 feval 版本，再考虑换写法（显式传泛型实参，或在工程里加一个独立入口方法）。

#### 数值字面量与 `.Equals()` 的悄默 False 陷阱

`MatchArgumentAndParameterTypes` 用 `IsAssignableFrom` 判形参兼容性。**对值类型，`IsAssignableFrom` 不做 C# 隐式扩展**——`typeof(long).IsAssignableFrom(typeof(int))` 返回 `false`，即使在 C# 里 `int → long` 编译期就允许。后果对**数值字段调 `.Equals()` 比较时尤其刺眼**：

```text
# Logic.LPlayer.I.PlayerID 是 System.Int64（long）
Logic.LPlayer.I.PlayerID.Equals(26124)
  # ❌ 返回 False —— 但值确实是 26124！
```

**根因链**：
1. 字面量 `26124` 是 `Int32`（不超 int 范围时默认 int）。
2. `long` 上有两个候选 `Equals` 重载：`long.Equals(long)` 和 `object.Equals(object)`。
3. `IsAssignableFrom(typeof(int), typeof(long))` 返回 false → `long.Equals(long)` **被排除**。
4. `IsAssignableFrom(typeof(int), typeof(object))` 返回 true → 命中 `object.Equals(object)` 重载，把 `Int32 26124` 装箱进 object。
5. CLR 的 `Int64.Equals(object obj)` 实现做精确类型比对：`obj is Int64 == false` → 直接返回 `false`，根本不比值。

**正确写法**：

```text
# 显式 Convert（推荐）
Logic.LPlayer.I.PlayerID.Equals(System.Convert.ToInt64(26124))    # ✅ True

# 用算术让结果晋升到 long（依赖 ReflectionUtilities.AddNumeric 的 GetHigherPriorityType）
Logic.LPlayer.I.PlayerID.Equals(12345678900 - 12345652776)        # ✅ True，差值是 long，超 int 范围
```

**适用范围**——这条不止 long.Equals(int)：任何值类型 A 字段 `.Equals(literal_of_type_B)` 当 A ≠ B 时几乎必踩。**uint/short/byte/sbyte/ushort/ulong** 都吃这套。
- `someByteField.Equals(0)` → False（`0` 是 int，byte ≠ int）
- `someUIntField.Equals(0)` → False（同理）
- 想避坑就把 RHS 字面量也强制和 LHS 同类型：`System.Convert.ToByte(0)` / `System.Convert.ToUInt32(0)` 等。

**为什么这条不命中『左/右都是 int 字面量』场景**：因为字面量 `1.Equals(2)` 时 LHS 也是 int，重载 `int.Equals(int)` 直接命中——只在「字段类型 ≠ 字面量类型」时咬人。

---

## 扩展方法

`Evaluator.EvaluateInvocationExpression` 在常规方法找不到时（**仅 instance 调用路径**，`!flag && methodInfo == null`）调用 `ExtensionMethodCache.FindExtensionMethod`。

### 扫描范围（重要：和你想的不一样）

```csharp
Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();   // 所有当前 AppDomain 加载的 assembly！
foreach (var asm in assemblies) {
    if (asm.IsDynamic) continue;
    foreach (var type in asm.GetTypes()) {
        if (!type.IsSealed || !type.IsAbstract || !type.IsClass) continue;   // sealed + abstract = static class
        foreach (var method in type.GetMethods(Static | Public | NonPublic))
            if (method.IsDefined(typeof(ExtensionAttribute), false))
                /* 候选 */
    }
}
```

**关键事实：**
- 扫描范围是 **`AppDomain.CurrentDomain.GetAssemblies()`**——**和 `WithReferences` 注册过的 imported assemblies 无关**。Unity 进程里所有当前已加载的 dll 都会被扫到，包括第三方插件、运行时动态加载的程序集。
- `assemblies()` 内置函数列出的列表（imported assemblies）**不代表扩展方法的可见范围**，比它大得多。
- 只看 `static class`（C# 的 `sealed + abstract`）。
- 同时找 `Public` 和 `NonPublic`——**internal / private 扩展方法也能命中**。

### 命中规则

#### 非泛型扩展方法路径（`FindExtensionMethod(targetType, methodName, parameterTypes)`）

- 第 0 个形参类型 `IsAssignableFrom(targetType)` 即视为可扩展类型（**不走继承/接口链**——这是浅匹配版本）。
- 后续形参一一与实参 `IsAssignableFrom`。
- 实参数 = 形参数 - 1。**没有默认参数 / 命名参数支持。**
- `FirstOrDefault`：多个候选时取第一个，不做最佳匹配评分。

#### 泛型扩展方法路径（`FindExtensionMethods(methodName, targetType, genericArgs, argTypes)`）

`Evaluator.EvaluateGenericInvocationExpression` 在 instance 路径用这条；以及非泛型 `EvaluateInvocationExpression` 当 `value.GetType().IsGenericType` 时也走这条。它支持得更聪明：

- 走 **`GetInheritanceChain(targetType)`**——target 类型 + 所有 BaseType 链，**还会检查 interfaces**。
- `IsParameterCompatible` 递归检查泛型 type definition 与具体化类型，所以 `IEnumerable<T>` 上的扩展方法能在 `List<int>` 上命中。
- 显式传入的泛型实参数必须和方法定义的泛型形参数一致；否则跳过该候选。

### 实务建议

- **写表达式时不需要 import 任何东西**。`UnityEngine.GameObject.Find("X").GetComponent<UI.Foo>()` 即使工程没在 imports 列表里加 `UnityEngine` 也跑得起来——`GetComponent<T>` 是 Component 上定义的方法（非扩展），但这个例子说明 Unity 自己的 dll 已经在 AppDomain 里。
- LINQ：`list.Where(...)` 这类**仍然不能用**，但**不是因为扩展方法找不到**——`System.Linq` 的扩展方法都在 AppDomain 里——而是因为 **lambda 语法 `x => ...` lexer 不支持**（`>` 被当 close-angle-bracket）。
- 同名扩展（`a.b.c.MyExt` 在 plugin 1 和 plugin 2 都定义了）→ FirstOrDefault 命中第一个；**不可控的非确定**。出现 "调到的不是你预期的那个 MyExt" 时考虑这种情况。
- **静态类的非扩展静态方法不能像扩展那样调**：必须 `OtherNs.MyStaticClass.MyMethod(...)`，不会自动加 `using`。

### 不走扩展方法的场景（最常被忽视的一节）

- **静态调用（`Type.Method`）从不查扩展方法**：`flag = (value == null) = true`，源码里 `if (!flag && methodInfo == null)` 才走扩展查找。所以——
  ```text
  System.Linq.Enumerable.Where(list, ...)   # 直接 static 调用扩展定义所在类，OK
  list.Where(...)                            # 实例链，会去找扩展，OK（但 lambda 解析失败是另一回事）
  
  Enumerable.MyExt(x)                        # 即使 MyExt 是 static class Enumerable 上的扩展，
                                             # static path 直接 GetMethod 查精确签名；
                                             # 找不到对 (T1) 的 static 重载（扩展形参是 (this T1)）→ Method not found
  ```
  **结论**：扩展方法的"语法糖"（让 `obj.MyExt(...)` 能调到 `static MyExt(this Obj o, ...)`）**只在实例链上展开**。要在 feval 里调扩展方法，**必须以实例链 `instance.Method(...)` 形式**写。
- 常规字段/属性访问：`obj.Field`、`obj.Prop` 没有"扩展属性"概念，从来不查扩展。
- `null.MyExt(...)`：value=null 时 `flag=true`，进入静态路径，找不到就抛——**没法对 null 调扩展**（即使该扩展是 `this object o` 形式）。

---

## out 参数

来自 `Parser.ParseOutExpressionSyntax`：

```csharp
new OutExpressionSyntax(..., ParseKeywordExpression(...), ParseIdentifierName());
```

**`out` 后只能跟 `IdentifierName`，不能跟 `var X`**——`var` 是关键字，会触发 `Syntax Error: unexpected token 'var' while IdentifierToken expected`。

```text
✅ d.TryGetValue("k", out v)           # v 不存在时也行，会被自动创建
❌ d.TryGetValue("k", out var v)       # CHANGELOG/README 写过此样例，但实际不支持
```

`v` 在调用前会被 `SetVariable(name, null)` 创建，调用后 `argumentValues[i]` 写回到该变量。

> **副作用：调用前 v 的旧值会被先清成 null**。`EvaluateInvocationExpression` 把每个 VariableSymbol 实参先替换为 null（`argumentValues[i] = null`），再记到 dictionary 里调用后回填。所以——
>
> ```text
> var v = "old"
> d.TryGetValue("k", out v)        # 不论 d 里有没有 "k"，v 在 invoke 期间一度是 null
> v                                 # 命中 → 新值；未命中 → 还是 null（不是 "old"）
> ```
>
> 一般无影响，但若 `out` 调用本身抛了异常，**v 就停在 null 上不会回滚**。

---

## 赋值语义（含索引器赋值的悄默失败）

`Evaluator.EvaluateAssignmentExpression` 按左侧 AST 节点类型分支：

| 左侧 AST | 写入路径 | 备注 |
|---|---|---|
| `IdentifierName x` | `m_Context.SetVariable(name, value)` | 变量不存在时**自动创建**——`x = 1` 等价 `var x = 1` |
| `DeclarationExpression var x` | 同上，但先 `CreateVariable` 再 SetVariable | 显式声明 |
| `MemberAccess obj.X`（obj 非 null） | `ReflectionUtilities.SetValue(obj, "X", value)`：先尝试 PropertyInfo.SetValue，没有则 FieldInfo.SetValue | **属性 setter 优先于字段写**——同名时 setter 拦截 |
| `MemberAccess Type.X`（左前缀解析为 Type，instance=null） | 同 `SetValue` 但用 Static flags | 静态字段/属性写入 |
| **`IndexAccess arr[k]`** | **没有 case，整个赋值方法什么也不做** | **悄默失败！见下** |
| 任何其他左侧（如 `(x)`、`a + b`） | 不命中任何分支 | 也悄默无效 |

### **致命陷阱：`arr[k] = v` 是 silent no-op**

`Evaluator.EvaluateAssignmentExpression` 的 if/else 链只覆盖 MemberAccess、Declaration、IdentifierName 三种左侧。`IndexAccess` 不在列。所以：

```text
var arr = new int[3]                  # 实际是 NRE，但忽略这个先
list[0] = 99                          # ⚠️ 返回 99，但 list 没变
dict["k"] = "v"                       # ⚠️ 同上
arr[1] = 42                           # ⚠️ 同上
```

返回值是右边求出的 `value`（看起来"成功"了），但**底层容器从未被写入**。读回 `list[0]` 仍是原值。

**对策：**
- 想给字典 / 列表写入，**写不了**——只能在工程里加一个 setter 方法或用反射手工调 `set_Item`：
  ```text
  dict.GetType().GetMethod("set_Item").Invoke(dict, new object[] { "k", "v" })
  ```
  但 `new object[]` 也不能直接构造数组字面量；改成临时变量串：
  ```text
  var args = System.Array.CreateInstance(typeof(object), 2)
  args.SetValue("k", 0)
  args.SetValue("v", 1)
  dict.GetType().GetMethod("set_Item").Invoke(dict, args)
  ```
  → 啰嗦但可行；不行就改工程加专用 setter。
- **数组**：`arr.SetValue(value, index)` 直接调 `Array.SetValue`，能写入：
  ```text
  arr.SetValue(42, 1)
  ```

### 静态成员写入

`Type.X = v` 走 MemberAccess 分支，`expression2.Value` 为 null（Type 不是值），转入 `expression2.TypeOrNamespace.Types.First().SetValue("X", value, instance: null)`——和读取一样，必须用 Type 名直接写，**不能 `instance.StaticField = v`**（同 instance/static binding flag 二选一问题）。

### `var x` 不带赋值会 NRE

Parser 把 `var x` 解析成 `DeclarationExpression(varKw, ParseExpression() as AssignmentExpressionSyntax)`。`x` 是 IdentifierName，**不是 AssignmentExpression**，as 强转返回 null。运行时 `EvaluateDeclarationExpression` 直接 `EvaluateExpression(null)` → NullReferenceException。

```text
var x                # ❌ NRE
var x = null         # ✅ 创建空变量
x = null             # ✅ 同上，var 可省（SetVariable 不存在时自动建）
```

要先建一个空槽位，等下面再赋值，**用 `var x = null`**，不要用 `var x;`。

---

## 索引访问

`expr[key]` → `value.GetType().InvokeMember(name, ...)`：

| 容器 | 调的成员 | 备注 |
|---|---|---|
| 数组 (`type.IsArray`) | `GetValue` | 调 `Array.GetValue(int)` |
| 其他 | `get_Item` | 字典、列表、自定义 indexer 都走这个 |

容器为 null → 抛 `NullReferenceException`（这是为数不多 feval 主动 NRE 的地方）。

**字符串索引 `"abc"[0]` 不支持**：返回 `Method 'String.get_Item' not found.`。原因：`string` 的 indexer 在 CLR 里被 `[IndexerName("Chars")]` 重命名，**`get_Item` 在 String 上不存在**，反射查不到。替代：

```text
"abc".Substring(0,1)        # 拿单字符的字符串
"abc".ToCharArray()[0]      # 拿 char（注意 char 比较仍然不能用 ==）
"abc".get_Chars(0)          # 直接用底层方法名
```

**`get_Item` 重载选择**不走 `MatchArgumentAndParameterTypes` 那条路径，而是 `InvokeMember` 内置的重载分发——所以参数类型隐式匹配略宽松（比如 `dict[1]` 在 `Dictionary<long, X>` 上能用，feval 会装箱再让 InvokeMember 处理）。

赋值 `arr[k] = v` 见上面"赋值语义"节——**是 silent no-op，不会写入**。

---

## typeof

`typeof(T)` → 评估内部表达式，取 TypeOrNamespace.Types.First。

```text
typeof(int)                           => System.Int32     # int 在 TypeKeywords
typeof(System.Int32).Name             => "Int32"           # 可以链式 .Member
typeof(System.Boolean)                => System.Boolean
typeof(bool)                          => Sequence contains no elements   # bool 不是 TypeKeyword
typeof(SomeNs.SomeType)               => System.RuntimeType
```

**关键字限制**：只有 `string` `int` `float` `long` `double` 五个走 TypeKeyword 通道；其他基础类型必须用 FQN。

---

## using 指令

`using SomeNs`（无分号、无引号、单独一行）：

- 把 `SomeNs` 加到当前 Context 的 `m_UsingNamespaces`。
- 之后 `IdentifierName` 解析时会按每个 using 加前缀去 `LookupTypes` 找。
- **持续到进程结束**（Context 是单例）。

> 多 using 不能写一行：`using A; using B` 卡在 `;` 的 `Illegal character: ;`，且 `using` 本身只接收一个 IdentifierName（不接受 `;` 后面再来一个）。每行一个。

`usings()` 内置函数列出当前所有 using。Service 模式启动时**为空**。

---

## 字符串内插

`$"text {expr} more"` → 来自 `Evaluator.EvaluateStringInterpolationExpression`：

```csharp
MatchCollection matchCollection = Regex.Matches(text, "{.+?}");
foreach (Match item in matchCollection) {
    stringBuilder.Append(text.Substring(num, item.Index - num));
    stringBuilder.Append(Evaluate(item.Value.Substring(1, item.Value.Length - 2), out var _).Value);
    num = item.Index + item.Length;
}
```

**关键点：**
- 用正则 `{.+?}` 找内插段（**非贪婪**），里面的内容**作为新的 feval 表达式**递归求值。
- **没有 `{{`/`}}` 转义**：`$"a {{b}}"` 会把 `{{b}` 当成内插表达式去求值 `{b`，触发 `Illegal character: {`。要表达字面 `{` 的字符串只能拼接 `"a " + ((char)123) + "b"` 之类。
- **内插表达式不能再含 `{` `}`**——非贪婪正则匹配到第一个 `}` 就闭合，剩下的 `}` 直接落进字符串原文，下一个 `{` 又开新一轮 → `Illegal character: {`。
  ```text
  $"id={Logic.LPlayer.I.PlayerID}"                 # ✅
  $"chain={a.b.M({c})}"                            # ❌ 内插体含 { } —— 拆成两步
  var inner = a.b.M(c)
  $"chain={inner}"                                 # ✅
  ```
- 内插表达式里一样不能写分号、`==` 等。
- 求值结果通过 `StringBuilder.Append(obj)` 落到串里，**`Append` 对 null 不抛错（写空串）**——和 `+` 拼接 null 报 ANE 不一样（见下方运算符 null 行为节）。所以**`$"...{maybeNull}..."` 是对 null 字段最安全的拼接形式**。

---

## 变量与 ans

- `var x = 42` 或 `x = 42`（`var` 可省）：声明 + 赋值。
- `x` 单独一行：读值。
- `ans`：内置变量，每次 `Evaluate` 完成后 `SetVariable("ans", result.Value)`。**注意 ans 在 Service 模式下也是 process 共享**——多个连接会互相覆盖对方的 ans。
- `vars()`：列当前 Context 的所有变量（除了 ans）。

**变量是 Context 单例的**：你 `var t = SomeMgr.LoadAsync(...)`，下次连接（同进程）`t` 仍然在，并且任何其他人也能看到/覆盖。**临时变量请加前缀**（如 `__probeFoo`）避免冲突。

---

## 内置函数全表

来自 `Feval/BuiltinFunctions.cs`（`[Builtin]` attribute 装饰的 `private static` method）。返回 `BuiltinFunctionResult`，被 `Evaluator` 解包为 `.Value`。**必须用括号调用**：

| 函数 | 签名 | 内部实现 |
|---|---|---|
| `help()` | `→ BuiltinFunctionResult(string)` | 输出版本信息 + copyright + 所有 `[Builtin]` 的 Name & Help。 |
| `dump(obj)` | `(object) → string` | `ObjDumper.Dump(obj)` → 默认调 `obj?.ToString() ?? "null"`，可被 `Context.RegisterDumper` 替换。**等价于一元 `` `obj ``。** |
| `usings()` | `→ string` | 输出 `"Using Namespaces:\n"` + 各 using 的 `\n` join。**注意：从 `Context.Main.UsingNamespaces` 读**——named context 看不到（但本 skill 用 Service 模式，本来就是用 Main）。 |
| `vars()` | `→ string` | 输出 `"Local Variables:\n"` + 每行 `"name -> value.ToString()"`。**会跳过 `ans`**（被 `m_Symbols` 过滤掉了）。 |
| `assemblies()` | `→ string` | 列已 `WithReferences` 注册到 Main Context 的 Assembly。**这只是"显式 import 的"那批，扩展方法的扫描范围比这大得多**——见"扩展方法"节。 |
| `version()` | `→ string` | `Feval.Core <major>.<minor>.<build>` |
| `copyright()` | `→ string` | `<assembly's [AssemblyCopyrightAttribute]> Feval.Core` |

**重要：写名字不加括号 ≠ 调用**

```text
help              # 拿到 FunctionSymbol 对象，ToString 得 Symbol 类全名（不是帮助文本）
help()            # 真的执行 → 帮助文本
```

**别名**：`` `obj `` 等价 `dump(obj)`（Lexer 1.2.0 起加的 BackquoteToken）。

**`Context.RegisterDumper(Func<object,string>)`**：项目里有时候会替换默认 dumper（比如把 `IList` 序列化成 JSON）。如果你 `` `someList `` 拿到看起来像 JSON 的串，就是项目自定义 dumper 的输出，**不是 feval 内置能力**。

---

## 明确不支持的语法（全清单）

按出现频率从高到低排：

1. **括号分组** `(a + b) * c` — 没有；改用变量分两步。
2. **比较运算符** `==` `!=` `<` `>` `<=` `>=` — `==` 词法识别但被丢；其他直接 `Illegal character`。
3. **逻辑运算符** `&&` `\|\|` `!` — 无。改写：用 if-style 的工程方法、或拆条件分别探。
4. **位运算** `&` `^` `<<` `>>` — 全无；只有 `\|`。
5. **C 风格强转** `(int)x` — 无。改用 `System.Convert.ToInt32(x)` 或 `x.ToInt32(...)`。
6. **类型测试 / 转换** `is` `as` — 词法 OK 但作 identifier 处理，被丢。改用 `obj.GetType() == typeof(T)` —— 但 `==` 也无效……所以实务上：`obj.GetType().FullName` 拿到字符串然后人眼判断。
7. **null 相关** `??` `?.` `??=` — 无。
8. **三元** `a ? b : c` — 无。
9. **Lambda / LINQ query** `x => x+1`、`from x in xs select x` — 无。`.Where(x => ...)` 在 lexer 看到 `>` 后认为是 close-angle-bracket 直接 Syntax Error。
10. **控制流** `if/for/while/foreach/switch/return/break/continue/try/catch/throw` — 无；feval 是单表达式求值器。
11. **多语句 / 语句块** `{ ... }`、`stmt; stmt` — 无；`;` 报 `Illegal character`，`{` 报 `Illegal character`。
12. **数组初始化** `new int[]{1,2,3}` `new[]{1,2,3}` — 无（`{` 直接错）。
13. **集合初始化** `new List<int>{1,2}` — 无。
14. **泛型构造的 size 形** `new int[3]` — 解析成 `new int` 索引访问，运行时 NRE。
15. **out var** `out var v` — 无；只能 `out v`。
16. **命名实参** `Method(name: value)` — 无（`:` 报 `Illegal character`）。
17. **可空类型语法** `int?` — `?` 报 `Illegal character`。
18. **`nameof(x)`** — `nameof` 是普通 identifier；`(x)` 不可作 grouping → Syntax Error。要拿名字只能写字符串字面量。
19. **`async/await`** — `await x` 把 `await` 当 identifier，且没有 await 语义。直接调返回 `Task`/`UniTask` 的方法只能拿到 task 对象。
20. **正则字面量、动态等** — 完全不存在。
21. **字符串转义 `\n` `\t` 等** — 直接是字面 2 字符。

---

## 运行时悄默失败（看似成功的陷阱）

这些**不报错、但语义错**，是最危险的一类。开发者会以为表达式跑对了：

| 表达式 | 期望 | 实际 |
|---|---|---|
| `1 == 2` | 比较 | 返回 `1`（LHS） |
| `"a" == "b"` | 比较 | 返回 `"a"` |
| `true == false` | 比较 | 返回 `True`（即 LHS bool 的 ToString） |
| `1.5f` | float 后缀 | `1.5`（`f` 被吃掉无效果） |
| `1 < 2` | 比较 | Syntax Error（`<` 被当 generic 起始） |
| `obj is SomeType` | 类型测试 | 返回 obj（`is` 当作 identifier，下面整个被丢） |
| `obj as SomeType` | 类型转换 | 同上 |
| `await someTask` | 等任务 | 返回 someTask 本身（task 对象） |
| **`list[0] = 99`** | 写入容器 | **返回 `99`，但 `list[0]` 仍是原值（Evaluator 没有 IndexAccess 的 Assignment 分支）** |
| **`dict["k"] = "v"`** | 写入 | **同上，silent no-op** |
| **`instance.StaticField = v`** | 写入静态 | **抛 `Member not found`**（看似不像悄默，但容易误以为在通过实例写）；要走 `Type.StaticField = v` |
| `help` 不带括号 | 调用 help | 返回 FunctionSymbol 对象（必须 `help()`） |
| `1.foo` | 想表达 `1` 的 `.foo`？没意义 | 被切成 `(1.0f)` + `.foo` → `Member not found`（lexer 把 `1.` 一起吞了） |
| `obj.Method`（不带括号） | 想拿方法引用 | **抛 `Member 'Method' not found`**——MemberAccess 只查字段/属性，不识别方法 |
| `"errMsg=" + ack.errMsg`（errMsg 为 null） | 拼日志容错 | **抛 `ArgumentNullException`**——`OperatorAdd` 入口 `ThrowIfArgumentIsNull`。改用 `$"errMsg={ack.errMsg}"` 内插 |
| `var x` | 创建一个空变量 | **运行时 NRE**（DeclarationExpression 拿到 null 的 AssignmentExpressionSyntax）。改用 `var x = null` |
| `Mgr.Get<List<int>>()` | 嵌套泛型实参 | **Syntax Error**——内层 `List<int>` 后必须是 `(`，但实际是 `>` |
| `longField.Equals(26124)` | 用字面量比较 long 字段 | **返回 `False`**（即使值真等！）——`IsAssignableFrom` 不做 int → long 隐式扩展，命中 `Equals(object)` 重载里的精确类型比对。改用 `longField.Equals(System.Convert.ToInt64(26124))` |

**对策**：
- 写 feval 表达式时**永远不写 `==` / `<` / `>` 等比较运算符**；改用方法调用（`a.Equals(b)`、`a.CompareTo(b)`）。
- 写完一条想"判断"的表达式后，先用最小 case 验证一下你以为它在做的事是不是真在做。例如先单独 `1.Equals(1)` 看返回 `True`，再用同形式表达条件。
- 异步：先 `var t = ...` 拿到 task，再读 `t.Status` / `t.GetAwaiter().IsCompleted` / `t.Result`。
- **修改容器**：能不在 feval 里改就别改（让开发者加 setter 方法 / 重启加载新数据）；非要改就用 `arr.SetValue(v, i)` 或 `dict.GetType().GetMethod("set_Item").Invoke(...)`，**绝不要写 `dict[k] = v`**。

---

## Reflector 自定义钩子与 IL2CPP

### 可替换的 Reflector

`Context.RegisterReflector(IReflector)` 允许工程提供自定义反射实现：

```csharp
public interface IReflector {
    Type GetType(Assembly assembly, string name);
    FieldInfo GetField(Type type, string name, BindingFlags flags);
    PropertyInfo GetProperty(Type type, string name, BindingFlags flags);
    IEnumerable<MethodInfo> GetMethods(Type type, string name, BindingFlags flags);
}
```

默认 `DefaultReflector` 直接调 `assembly.GetType()` / `type.GetField` 等。**项目可以注册一个自定义 reflector 来：**
- 在 IL2CPP 上把 `GetType("Foo")` 重定向到 HybridCLR 加载的程序集；
- 把字段名映射改写（比如混淆过的字段在调试时映射回原名）；
- 给 IL2CPP 被裁掉的成员手工补充元数据。

**Service 模式查找当前 reflector**：调 `Context.Main` 的 `RegisterReflector` 是 setter，没有 getter；想知道用的是哪个 IReflector 实现，得到工程源码里 grep `RegisterReflector(`。

### IL2CPP 真机注意

包内 `link.xml` 防 `Feval.Core.dll` 自身被裁，但**目标类型/成员**仍可能被 Unity 链接器剥：

| 失败模式 | 现象 | 对策 |
|---|---|---|
| 字段被裁 | `Member 'XxxField' not found` | 在工程 `Assets/link.xml` 里给目标类型加 `<type fullname="Some.Ns.Foo" preserve="all"/>`，或类型上加 `[UnityEngine.Scripting.Preserve]` |
| 方法被裁 | `Method 'Foo(...)' not found` | 同上 |
| 整个类型不存在 | `Type or namespace Some.Ns.Foo not found` | 类型从未被静态引用过；`link.xml` 整段加 |
| 泛型构造失败 | `Failed to find generic method: ... ` | AOT 缺少泛型实例化代码；HybridCLR 工程要在 `AOTGenericReferences.cs` 里加引用 |
| Reflection 抛 `ExecutionEngineException` | Unity AOT 反射限制 | 同上 |

`assemblies()` 里看不到的程序集**不一定不能反射访问**——扩展方法和裸 Type lookup 都走 `AppDomain.CurrentDomain.GetAssemblies()`，比 `assemblies()` 列表大。但反过来，`Type or namespace not found` 里如果是个明明应该有的类型，最大可能是被 IL2CPP 裁掉了。

### 验证 reflection 工作的最小命令

连接到真机 Service 后，先发这几条确认基础反射正常：

```text
typeof(int).Name                               # 期望 "Int32"
System.AppDomain.CurrentDomain.GetAssemblies().Length   # 期望几十～几百
UnityEngine.Application.isMobilePlatform       # 期望 True
```

任何一条出错说明该真机的反射环境有问题，再排除目标类型 `[Preserve]` 等问题没意义。

---

## 包装脚本特有约定

本 skill 的 `feval_runtime_debug.py` / `.ps1` 在 `--commands-file` 模式下做了 feval 自身**没有**的预处理：

| 行内容 | 处理 |
|---|---|
| `# 起头` | **跳过**（注释）—— 见 `feval_runtime_debug.py:139` |
| 空行/纯空白 | **跳过** |
| 其他 | 作为一条 feval 表达式提交 |

> **直接调用 `feval -e "# foo"` 会报 `Illegal character: #`**——`#` 注释纯属包装层约定，不是 feval 语言特性。

文件编码必须 **UTF-8 无 BOM**（feval 把 BOM 当成 `?`）；详见主 SKILL.md "临时命令文件落点"节。

---

## Service 网络协议

来自 `EvaluationService.cs` + `EvaluationClient.cs`：

- TCP 监听端口（默认 9999）。Client 发送 **原始 UTF-8 字节**，内容就是表达式文本（无包封、无 length prefix）。
- Server 端把请求扔进 `m_Queue`，由 `Update()` 在主线程逐条 `Evaluate`，把结果再以原始 UTF-8 字节回发：
  - **有返回值**：`result.ToString()` 的字节
  - **void 调用**：固定字符串 `$NoReturn`
  - **抛异常**：`exception.Message` 的字节
- UDP 探测端口 `11111`（监听） + `11112`（发送）：客户端发 `DETECTION-<myip>` → 服务回 `<deviceName>|<localIp>|<port>`。

> **结论**：上层 CLI（`Feval.Cli`）做了协议增强（多句、`#load file` 加载、连接别名等）；本 skill 的 `--no-console-fallback` 走的就是 stdio CLI。需要纯协议级控制时直接打 TCP 也行，但失去重试 / 编码 / 错误格式化。

---

## 源码定位与反编译

文档归纳了已知行为，但 feval 还有未公开 / 未文档化的角落（罕见 token、ReflectionUtilities 内部分支、CLI 协议增强细节等）。本节没覆盖、Top 表也没命中时，**直接读源**。

### 路径（相对工程根 `G:\launchpad\client\`）

包根：`Library/PackageCache/com.tfw.feval@1.3.1/`

| 文件 | 干什么 | 什么时候读它 |
|---|---|---|
| `Runtime/EvaluationService.cs` | 游戏内 Service（端口 9999），把请求扔主线程 `m_Queue` → `Evaluate` → 回写结果 | 想搞清"feval 连进游戏的那条线" / 多请求阻塞 / 主线程时机 |
| `Runtime/EvaluationClient.cs` | 客户端 socket / 协议封装（CLI 也用这套） | TCP 协议字节级问题；`stderr` 里看到奇怪连接错误 |
| `Runtime/UdpDetector.cs` | UDP 11111/11112 自动发现 | `feval` 连不上但端口在监听；想做局域网批量发现 |
| `Runtime/Plugins/Feval.Core.dll` | **核心解析与求值器**（`SyntaxTree.Parse` / `Evaluator` / `ReflectionUtilities` / `ExtensionMethodCache`） | 本文档里所有行为追溯到的 dll；**只能反编译**——无 C# 源 |
| `Runtime/Plugins/link.xml` | IL2CPP 防裁配置（仅 dll 自身，不含项目类型） | 真机运行时 `Method/Member not found` 想看默认保留范围 |
| `Editor/FevalEvaluator.cs` | Editor `Window/Feval` 菜单背后的 evaluator wrapper（默认 using `UnityEngine`/`UnityEditor`/`System`） | 解释为何"Editor 里能跑、Service 里报 Symbol not found" |
| `Editor/Host.cs` `HostTreeView.cs` `IPInputBox.cs` `RemoteDetectorEditor.cs` | Editor UI（设备列表、IP 输入框） | 一般用不到；Editor UI 行为异常时再看 |
| `CHANGELOG.md` `README.md` | 版本说明 | **仅供参考**——存在与实际行为出入，文档里"以本文档为准" |

### 反编译 `Feval.Core.dll`

包里就是个二进制 dll，要看实现必须反编译。推荐工具（Windows 上任选其一）：

- **ILSpy**（图形 / 命令行；命令行装 `dotnet tool install -g ilspycmd`）
  ```powershell
  ilspycmd "G:\launchpad\client\Library\PackageCache\com.tfw.feval@1.3.1\Runtime\Plugins\Feval.Core.dll" -o "$env:TEMP\feval-decomp"
  ```
- **dnSpy / dnSpyEx**：双击 dll 直接看，可调试。
- **JetBrains dotPeek**：装了 Rider/ReSharper 已带。

反编译产物里关注的入口符号（出问题时优先 grep）：

| 想搞清的现象 | grep 这个符号 |
|---|---|
| `==` `<` `>` 后面被吞 / 整体只回左操作数 | `m_BinaryOperatorPriorities` `ParseBinaryExpression` `GetBinaryOperatorPriority` |
| `Illegal character: X` 报错 | `Lexer` `ScanToken` `IllegalCharacter` |
| `Symbol 'X' not found` | `ResolveIdentifier` `LookupTypes` `LookupNamespaces` |
| `Member 'X' not found` | `EvaluateMemberAccessExpression` `GetField` `GetProperty` |
| `Method 'X' not found` / `does not overload` | `EvaluateInvocationExpression` `MatchArgumentAndParameterTypes` `GetMethods` |
| 数值类型晋升 / `OverflowException` | `ReflectionUtilities` `m_NumericTypes` `GetHigherPriorityType` `AddNumeric` |
| `arr[k] = v` 看似成功 | `EvaluateAssignmentExpression` `IndexAccessExpression` |
| 字符串内插对 null 的处理 | `EvaluateInterpolatedStringExpression` `StringBuilder` |
| 扩展方法找不到 | `ExtensionMethodCache` `FindExtensionMethod` |

### 工作流建议

1. **现象 → 本文档症状速查 → 章节子节** 命中就停，不用反编译。
2. 没命中或细节对不上 → **反编译 dll → 按上表 grep 入口符号 → 顺源读流程**。
3. 修源码不属于本仓库职责（feval 是上游包）；发现 bug 且确需修，到 <https://git.tap4fun.com/tfw/com.tfw.feval> 提 issue / MR，并把变更同步回本文档"以本文档为准"的描述。
4. **不要**把反编译产物落到本仓库 / 临时文件落点应在 `%TEMP%\feval-decomp\`，避免被 git 追到。
