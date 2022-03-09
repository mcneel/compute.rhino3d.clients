⚠ _This repository is a work-in-progress experiment to merge [computeclient_js](https://github.com/mcneel/computeclient_js), [computeclient_py](https://github.com/mcneel/computeclient_py) and [computegen](https://github.com/mcneel/compute.rhino3d/tree/master/src/compute.client).._ 🧪

# Rhino Compute Clients

<!-- [![NuGet](https://img.shields.io/nuget/v/compute-rhino3d.svg?style=flat)](https://www.nuget.org/packages/compute-rhino3d) -->
[![PyPI](https://img.shields.io/pypi/v/compute-rhino3d.svg)](https://pypi.org/project/compute-rhino3d)
[![npm](https://img.shields.io/npm/v/compute-rhino3d.svg)](https://www.npmjs.com/package/compute-rhino3d)

[![Discourse users](https://img.shields.io/discourse/https/discourse.mcneel.com/users.svg)](https://discourse.mcneel.com/c/rhino-developer/compute-rhino3d/90)

This repository contains the source code and documentation for the .NET, Python and JavaScript client libraries used to communicate with [Rhino Compute](https://github.com/mcneel/compute.rhino3d).

We use code-generation to create these libraries. This is handled by _computegen_, also in this repository.

For more, see the [guides on the Rhino Developer Docs site](https://developer.rhino3d.com/guides/compute/).

## Development

run computegen

`dotnet run --project src/computegen.csproj`
