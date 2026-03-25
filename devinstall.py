import os
import shutil

os.chdir(os.path.expanduser("~"))

dst_path = os.path.join("Documents", "workspace", "microting", "eform-debian-service", "Plugins", "ServiceMonitoringPlugin")
src_path = os.path.join("Documents", "workspace", "microting", "eform-service-monitoring-plugin", "ServiceMonitoringPlugin")

if os.path.exists(dst_path):
    shutil.rmtree(dst_path)

os.makedirs(os.path.dirname(dst_path), exist_ok=True)

shutil.copytree(src_path, dst_path)
