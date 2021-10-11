#!/bin/bash

#############################################
# Created By CyperPool for CyberCore use... #
#############################################

source /etc/functions.sh
source /etc/web.conf

if [[ ("$Install_SSL" == "y" || "$Install_SSL" == "Y" || "$Install_SSL" == "yes" || "$Install_SSL" == "Yes" || "$Install_SSL" == "YES") ]]; then
        hide_output bash $HOME/ssl.sh
fi
else
cd $HOME/

