
# 🚀 Azure Functions In-Proc Response Compression (POC)

This repository contains a **proof of concept (POC)** demonstrating how to implement **response compression** (Gzip and Brotli) in **Azure Functions (In-Process model)**. It enables HTTP-triggered functions to return compressed responses based on the `Accept-Encoding` header.

---

## 📌 Purpose

Optimize Azure Function responses by reducing payload sizes, improving transfer speeds, and supporting clients with modern compression algorithms.

---

## ✨ Features

- ✅ In-process Azure Functions using **.NET 8**
- ✅ Supports **Gzip** and **Brotli** compression
- ✅ Detects `Accept-Encoding` from incoming requests
- ✅ Applies dynamic compression on JSON/text payloads
- ✅ Lightweight and extensible structure

---

## 🧱 Project Structure

```
ResponseCompression/
├── Properties/
│   ├── launchSettings.json
│   └── serviceDependencies.json
├── Extensions/
│   └── AzureFunctionsInProcessCompressionExtensions.cs
├── Functions/
│   └── Function1.cs
├── GlobalUsings.cs
├── host.json
├── local.settings.json
├── .gitignore
└── README.md
```

---

## 🏗️ Tech Stack

- **Azure Functions (In-Proc)**
- **.NET 8**
- **System.IO.Compression**
- **System.Text.Json**

---

## 🚀 Running Locally

### 🔧 Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Visual Studio 2022+](https://visualstudio.microsoft.com/) or VS Code

### ▶️ Start Function App

```bash
func start
```

By default, the function runs on:  
**`http://localhost:7169`**

---

## 📤 Example Usage with `curl`

### 🗜️ Gzip

```bash
curl -H "Accept-Encoding: gzip" http://localhost:7169/api/GetCompressedResponse --output response.gz
```

### 🗜️ Brotli

```bash
curl -H "Accept-Encoding: br" http://localhost:7169/api/GetCompressedResponse --output response.br
```

### 🧾 No Compression

```bash
curl http://localhost:7169/api/GetCompressedResponse
```

---

## 🧠 How It Works

1. HTTP-triggered function receives request
2. Checks `Accept-Encoding` header
3. Applies compression (Gzip or Brotli) using `System.IO.Compression`
4. Sets correct `Content-Encoding` and `Content-Type`
5. Returns compressed stream in response

Compression logic is encapsulated inside:
```
Extensions/AzureFunctionsInProcessCompressionExtensions.cs
```

---

## 📚 References

- [Azure Functions HTTP Trigger](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-http-webhook)
- [GzipStream Docs](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.gzipstream)
- [BrotliStream Docs](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream)

---

## ❗ Limitations

- Works with **in-process model** only
- Not suitable for binary content (e.g., images)
- Not using built-in ASP.NET Core middleware

---

## 📄 License

MIT License. See [LICENSE](LICENSE) for details.

---

## 🙌 Contributions

Feel free to fork, extend, and improve this POC. PRs welcome!
