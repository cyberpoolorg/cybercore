#!/bin/bash

#############################################
# Created By CyperPool for CyberCore use... #
#############################################

source /etc/functions.sh
source /etc/web.conf

if [[ ("$Using_Sub_Domain" == "yes") ]]; then
	cd $HOME/extra
	source subdomain.sh
	if [[ ("$Install_SSL" == "yes") ]]; then
		cd $HOME/extra
		source subdomain_ssl.sh
	fi
	else
	cd $HOME/extra
	source domain.sh
	if [[ ("$Install_SSL" == "yes") ]]; then
		cd $HOME/extra
		source domain_ssl.sh
	fi
fi

cd $HOME/