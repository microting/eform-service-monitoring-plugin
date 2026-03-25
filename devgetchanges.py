import os
import shutil

os.chdir(os.path.expanduser("~"))

dst_path = os.path.join("Documents", "workspace", "microting", "eform-service-monitoring-plugin", "ServiceMonitoringPlugin")
src_path = os.path.join("Documents", "workspace", "microting", "eform-debian-service", "Plugins", "ServiceMonitoringPlugin")

if os.path.exists(dst_path):
    shutil.rmtree(dst_path)

shutil.copytree(src_path, dst_path)
