#!/bin/bash

cd ~

if [ -d "Documents/workspace/microting/eform-service-monitoring-plugin/ServiceMonitoringPlugin" ]; then
	rm -fR Documents/workspace/microting/eform-service-monitoring-plugin/ServiceMonitoringPlugin
fi

cp -av Documents/workspace/microting/eform-debian-service/Plugins/ServiceMonitoringPlugin Documents/workspace/microting/eform-service-monitoring-plugin/ServiceMonitoringPlugin
