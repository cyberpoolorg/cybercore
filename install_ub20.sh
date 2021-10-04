#!/bin/bash
################################################################################
# Author: CyberPool                                                            #
#                                                                              #
# Web: https://cyberpool.org                                                   #
#                                                                              #
# Program:                                                                     #
#   Install CyberCore on Ubuntu 20.04 running Nginx, Dotnet 5, and Postgresql  #
#   v0.1 (update October, 2021)                                                #
#                                                                              #
################################################################################

    sleep 2

    output() {
	    printf "\E[0;33;40m"
	    echo $1
	    printf "\E[0m"
    }

    displayErr() {
	    echo
	    echo $1;
	    echo
	    exit 1;
    }

    wget -L https://raw.githubusercontent.com/cyberpoolorg/cybercore/master/extra/functions.sh
    sudo cp -r functions.sh /etc/

    source /etc/functions.sh

    clear
    echo
    echo -e "$GREEN***************************************************************************$COL_RESET"
    echo -e "$GREEN* CyberCore Install Script v0.1                                           *$COL_RESET"
    echo -e "$GREEN* Install CyberCore on Ubuntu 20.04 running Nginx, Dotnet 5, and Postgres *$COL_RESET"
    echo -e "$GREEN***************************************************************************$COL_RESET"
    echo
    sleep 3


    # Update package and Upgrade Ubuntu
    echo
    echo
    echo -e "$CYAN => Updating System And Installing Required Packages $COL_RESET"
    echo 
    sleep 3
        
    hide_output sudo apt -y update 
    hide_output sudo apt -y upgrade
    hide_output sudo apt -y autoremove
    apt_install apt-transport-https build-essential software-properties-common curl unzip rar htop git
    apt_install libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq3-dev libzmq5 screen cmake
    apt_install certbot python3-certbot-nginx dialog
    echo -e "$GREEN Done...$COL_RESET"

    echo
    echo
    echo -e "$RED Make sure you double check before hitting enter! Only one shot at these! $COL_RESET"
    echo
    echo -e "$CYAN => Please Enter PSQL Password $COL_RESET"
    read postgres_pass

    # Installing Nginx
    echo
    echo
    echo -e "$CYAN => Installing Nginx server : $COL_RESET"
    echo
    sleep 3
    
    if [ -f /usr/sbin/apache2 ]; then
    echo -e "Removing apache..."
    hide_output apt-get -y purge apache2 apache2-*
    hide_output apt-get -y --purge autoremove
    fi

    apt_install nginx
    hide_output sudo systemctl start nginx.service
    hide_output sudo systemctl enable nginx.service
    hide_output sudo systemctl start cron.service
    hide_output sudo systemctl enable cron.service
    sleep 5
    sudo systemctl status nginx | sed -n "1,3p"
    echo
    echo -e "$GREEN Done...$COL_RESET"

    # Making Nginx a bit hard
    echo 'map $http_user_agent $blockedagent {
    default         0;
    ~*malicious     1;
    ~*bot           1;
    ~*backdoor      1;
    ~*crawler       1;
    ~*bandit        1;
    }
    ' | sudo -E tee /etc/nginx/blockuseragents.rules >/dev/null 2>&1


    # Installing Postgres
    echo
    echo
    echo -e "$CYAN => Installing Postgres : $COL_RESET"
    echo
    sleep 3

    apt_install postgresql postgresql-contrib
    sleep 5
    sudo systemctl status postgresql | sed -n "1,3p"
    echo
    echo -e "$GREEN Done...$COL_RESET"


    # Installing Dotnet 5
    echo
    echo
    echo -e "$CYAN => Installing Microsoft Dotnet 5.0 : $COL_RESET"
    echo
    sleep 3

    hide_output wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    hide_output sudo dpkg -i packages-microsoft-prod.deb
    hide_output sudo apt -y update 
    hide_output sudo apt -y upgrade
    apt_install dotnet-sdk-5.0
    sleep 5
    echo -e "$GREEN Done...$COL_RESET"


    # Installing Fail2Ban
    echo
    echo
    echo -e "$CYAN => Installing Fail2Ban $COL_RESET"
    echo
    sleep 3
    
    apt_install fail2ban
    sleep 5
    sudo systemctl status fail2ban | sed -n "1,3p"
    echo
    echo -e "$GREEN Done...$COL_RESET"


    # Installing UFW
    echo
    echo
    echo -e "$CYAN => Installing UFW $COL_RESET"
    echo
    sleep 3

    apt_install ufw
    hide_output sudo ufw allow ssh
    hide_output sudo ufw allow http
    hide_output sudo ufw allow https
    hide_output sudo ufw allow 'Nginx Full'
    hide_output sudo ufw allow 4000
    hide_output sudo ufw allow 3033/tcp
    hide_output sudo ufw allow 3133/tcp
    hide_output sudo ufw allow 3233/tcp
    hide_output sudo ufw allow 3333/tcp
    hide_output sudo ufw allow 3433/tcp
    hide_output sudo ufw allow 3533/tcp
    hide_output sudo ufw allow 3633/tcp
    hide_output sudo ufw allow 3733/tcp
    hide_output sudo ufw allow 3833/tcp
    hide_output sudo ufw allow 3933/tcp
    hide_output sudo ufw allow 4033/tcp
    hide_output sudo ufw allow 4133/tcp
    hide_output sudo ufw allow 4233/tcp
    hide_output sudo ufw allow 4333/tcp
    hide_output sudo ufw allow 4433/tcp
    hide_output sudo ufw allow 4533/tcp
    hide_output sudo ufw allow 4633/tcp
    hide_output sudo ufw allow 4733/tcp
    hide_output sudo ufw allow 4833/tcp
    hide_output sudo ufw allow 4933/tcp
    hide_output sudo ufw --force enable
    sleep 5
    sudo systemctl status ufw | sed -n "1,3p"
    echo
    echo -e "$GREEN Done...$COL_RESET"


    # Installing CyberCore
    echo
    echo
    echo -e "$CYAN => Installing CyberCore $COL_RESET"
    echo
    echo -e "Grabbing CyberCore fron Github and Build It"
    echo
    sleep 3

    cd ~
    hide_output git clone https://github.com/cyberpoolorg/cybercore.git
    chmod -R +x $HOME/cybercore/
    cd $HOME/cybercore/src/Cybercore
    hide_output dotnet publish -c Release --framework net5.0  -o ../../build
    echo -e "$GREEN Done...$COL_RESET"


    # Create DB
    echo
    echo
    echo -e "$CYAN => Creating DB $COL_RESET"
    echo
    echo -e "With Given Password At Start"
    echo
    sleep 3

    hide_output sudo -u postgres createuser --superuser cybercore
    hide_output sudo -u postgres psql -c "alter user cybercore with encrypted password '"$postgres_pass"';"
    hide_output sudo -u postgres createdb cybercore
    hide_output sudo -u postgres psql -c "alter database cybercore owner to cybercore;"
    hide_output sudo -u postgres psql -c "grant all privileges on database cybercore to cybercore;"
    hide_output PGPASSWORD="$postgres_pass" psql -d cybercore -U cybercore -h 127.0.0.1 -f $HOME/cybercore/src/Cybercore/Persistence/Postgres/Scripts/createdb.sql
    echo -e "$GREEN Done...$COL_RESET"


    echo
    echo
    echo -e "$GREEN********************************$COL_RESET"
    echo -e "$GREEN CyberCore Install Script v0.1 *$COL_RESET"
    echo -e "$GREEN Finish !!!                    *$COL_RESET"
    echo -e "$GREEN********************************$COL_RESET"
    echo 
    echo
    echo -e "$CYAN WoW that was fun, just some reminders. $COL_RESET"
    echo
    echo -e "$RED Your Postgresql User is cybercore $COL_RESET"
    echo -e "$RED Your Postgresql Database is cybercore $COL_RESET"
    echo -e "$RED Your Postgresql Password is "$postgres_pass" $COL_RESET"
    echo
    echo -e "$RED Yiimp at : http://"$server_name" (https... if SSL enabled)"
    echo -e "$RED Yiimp Admin at : http://"$server_name"/site/AdminPanel (https... if SSL enabled)"
    echo -e "$RED Yiimp phpMyAdmin at : http://"$server_name"/phpmyadmin (https... if SSL enabled)"
    echo
    echo -e "$CYAN Example Config Files Are In $HOME/cybercore/examples/ $COL_RESET"
    echo -e "$CYAN To Start Cybercore run : $HOME/cybercore/build/dotnet Cybercore.dll -c config.json $COL_RESET"
    echo
    echo
    echo -e "$RED***************************************************$COL_RESET"
    echo -e "$RED YOU MUST REBOOT NOW TO FINALIZE INSTALLATION !!! *$COL_RESET"
    echo -e "$RED***************************************************$COL_RESET"
    echo
    echo
