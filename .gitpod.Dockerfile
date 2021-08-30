FROM gitpod/workspace-dotnet-lts
RUN wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
RUN sudo dpkg -i packages-microsoft-prod.deb
RUN sudo apt-get update
RUN sudo apt-get install -y azure-functions-core-tools-3 curl
RUN wget -O - https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh
ENV NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED true