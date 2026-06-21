Remove-Item -Recurse -Force .\Migrations
Remove-Item -Force .\assignment_grader.db

dotnet ef migrations add InitialCreate
dotnet ef database update

dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef
dotnet tool update --global dotnet-ef