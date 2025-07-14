
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
- ✅ Uses **Newtonsoft.Json** for serialization
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
│   └── CompressedJsonFunction.cs
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
- **Newtonsoft.Json**

---

## 🚀 Running Locally

### 🔧 Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Visual Studio 2022+](https://visualstudio.microsoft.com/) or VS Code

### ▶️ Start the Function App

```bash
func start
```

By default, the function runs on:

```
http://localhost:7169
```

---

## 📤 Example Usage with `curl`

### 🗜️ Gzip

```bash
curl -H "Accept-Encoding: gzip" http://localhost:7169/api/compressed-json --output response.gz
```

### 🗜️ Brotli

```bash
curl -H "Accept-Encoding: br" http://localhost:7169/api/compressed-json --output response.br
```

### 🧾 No Compression

```bash
curl http://localhost:7169/api/compressed-json
```

---

## 🧠 How It Works

1. `CompressedJsonFunction` receives the HTTP request
2. Reads the `Accept-Encoding` header
3. Serializes the response using `Newtonsoft.Json`
4. Applies compression (Gzip or Brotli) using `System.IO.Compression`
5. Sets appropriate `Content-Encoding` and `Content-Type` headers
6. Returns the compressed stream as the response

➡️ Compression logic is encapsulated in:  
`Extensions/AzureFunctionsInProcessCompressionExtensions.cs`

---

## 📚 References

- [Azure Functions HTTP Trigger](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-http-webhook)
- [GzipStream Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.gzipstream)
- [BrotliStream Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream)
- [Newtonsoft.Json Documentation](https://www.newtonsoft.com/json/help/html/Introduction.htm)

---

## ❗ Limitations

- Designed for the **in-process model** only
- Not suitable for binary or large media content
- ASP.NET Core middleware pipeline is not available in in-proc model

---

## 🙌 Contributions

Feel free to fork, extend, and improve this POC. Pull requests are welcome!
```