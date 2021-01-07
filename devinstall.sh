#!/bin/bash

cd ~

if [ -d "Documents/workspace/microting/eform-debian-service/Plugins/ServiceMonitoringPlugin" ]; then
	rm -fR Documents/workspace/microting/eform-debian-service/Plugins/ServiceMonitoringPlugin
fi

mkdir Documents/workspace/microting/eform-debian-service/Plugins

cp -av Documents/workspace/microting/eform-service-monitoring-plugin/ServiceMonitoringPlugin Documents/workspace/microting/eform-debian-service/Plugins/ServiceMonitoringPlugin
