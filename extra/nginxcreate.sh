#!/bin/bash

#############################################
# Created By CyperPool for CyberCore use... #
#############################################

source /etc/functions.sh
source /etc/web.conf

echo -e "GREEN=> Generating Nginx Configs...$COL_RESET"
if [[ ("$Using_Sub_Domain" == "y" || "$Using_Sub_Domain" == "Y" || "$Using_Sub_Domain" == "yes" || "$Using_Sub_Domain" == "Yes" || "$Using_Sub_Domain" == "YES") ]]; then
  cd $HOME/extra
  source web_with_sub.sh
    if [[ ("$Install_SSL" == "y" || "$Install_SSL" == "Y" || "$Install_SSL" == "yes" || "$Install_SSL" == "Yes" || "$Install_SSL" == "YES") ]]; then
      cd $HOME/extra
      source web_with_sub_ssl.sh
    fi
      else
        $HOME/extra
        source web_without_sub.sh
    if [[ ("$Install_SSL" == "y" || "$Install_SSL" == "Y" || "$Install_SSL" == "yes" || "$Install_SSL" == "Yes" || "$Install_SSL" == "YES") ]]; then
      cd $HOME/extra
      source web_without_sub_ssl.sh
    fi
fi
cd ~