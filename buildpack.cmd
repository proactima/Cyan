mkdir release

@call msbuild Cyan\Cyan.csproj /t:clean
@call msbuild Cyan\Cyan.csproj /p:Configuration=Release

.nuget\NuGet.exe pack -sym Cyan\Cyan.csproj -OutputDirectory release

@echo Publish: .nuget\nuget push [package] [API Key] -Source https://www.myget.org/F/uxrisk/api/v2/package
