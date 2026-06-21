Remove-Item -Recurse -Force .\Migrations
Remove-Item -Force .\assignment_grader.db

dotnet ef migrations add InitialCreate
dotnet ef database update
