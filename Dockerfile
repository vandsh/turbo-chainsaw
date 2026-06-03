FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["TestProject.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Seed sample browse data
RUN mkdir -p /app/browse-data/documents /app/browse-data/images /app/browse-data/logs /app/browse-data/data/reports \
    && for i in $(seq 1 150); do \
         echo "Sample document content for file $i - $(date +%s%N)" > /app/browse-data/documents/doc-$(printf '%03d' $i).txt; \
         echo "{\"id\": $i, \"name\": \"record-$i\"}" > /app/browse-data/data/record-$(printf '%03d' $i).json; \
         echo "Log entry $i: INFO - Operation completed" > /app/browse-data/logs/app-$(printf '%03d' $i).log; \
         echo "<svg><text>image $i</text></svg>" > /app/browse-data/images/img-$(printf '%03d' $i).svg; \
         echo "report,$i,$(( i * 100 )),complete" > /app/browse-data/data/reports/report-$(printf '%03d' $i).csv; \
       done \
    && echo "Seeded 750 sample files (150 per folder)"

ENV ASPNETCORE_URLS=http://+:8080
# This could be an environment variable for the FileBrowser browse root, probably should be in a non-demo env
ENV FileBrowser__BrowseRoot=/app/browse-data
ENTRYPOINT ["dotnet", "TestProject.dll"]
