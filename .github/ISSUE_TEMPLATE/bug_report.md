---
name: Bug report
about: Create a report to help us improve

---

**Describe the bug**
A clear and concise description of what the bug is.

**Related Source Code to Reproduce**
If applicable, add the related source codes to help explain your problem, for example:
```csharp
var manager = new RtlSdrDeviceManager();
manager.OpenManagedDevice(0, "my-rtl-sdr");
manager["my-rtl-sdr"].CenterFrequency = new Frequency {MHz = 1090};
```

**Expected behavior**
A clear and concise description of what you expected to happen.

**Environment (please complete the following information):**
 - OS: [e.g. Fedora 27, Ubuntu 18.04]
 - .NET Core SDK or Runtime [e.g. 2.1]
 - Used RtlSdrManager version [e.g. 0.1.2]

**Additional context**
Add any other context about the problem here.
