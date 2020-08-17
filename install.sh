#!/bin/bash

if [ -d "/var/www/microting/eform-service-monitoring-plugin" ]; then
	rm -fR /var/www/microting/eform-service-monitoring-plugin
fi

cd /var/www/microting
su ubuntu -c \
"git clone https://github.com/microting/eform-service-monitoring-plugin.git -b stable"

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
"mkdir -p /var/www/microting/eform-debian-service/MicrotingService/out/Plugins/"

if [ -d "/var/www/microting/eform-debian-service/MicrotingService/out/Plugins/ServiceMonitoringPlugin" ]; then
	rm -fR /var/www/microting/eform-debian-service/MicrotingService/out/Plugins/ServiceMonitoringPlugin
fi

su ubuntu -c \
"cp -av /var/www/microting/eform-service-monitoring-plugin/out /var/www/microting/eform-debian-service/MicrotingService/out/Plugins/ServiceMonitoringPlugin"
