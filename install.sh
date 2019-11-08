#!/bin/bash

if [ ! -d "/var/www/microting/eform-service-monitoring-plugin" ]; then
  cd /var/www/microting
  su ubuntu -c \
  "git clone https://github.com/microting/eform-service-monitoring-plugin.git -b stable"
fi

cd /var/www/microting/eform-service-monitoring-plugin
su ubuntu -c \
"dotnet restore ServiceMonitoringPlugin.sln"

echo "################## START GITVERSION ##################"
export GITVERSION=`git describe --abbrev=0 --tags | cut -d "v" -f 2`
echo $GITVERSION
echo "################## END GITVERSION ##################"
su ubuntu -c \
"dotnet publish ServiceMonitoringPlugin.sln -o out /p:Version=$GITVERSION --runtime linux-x64 --configuration Release"

su ubuntu -c \
"mkdir -p /var/www/microting/eform-debian-service/MicrotingService/MicrotingService/out/Plugins/"

su ubuntu -c \
"cp -av /var/www/microting/eform-service-monitoring-plugin/ServiceMonitoringPlugin/out /var/www/microting/eform-debian-service/MicrotingService/out/Plugins/ServiceMonitoringPlugin"
