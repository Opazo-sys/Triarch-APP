# Triarch APP

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)
![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-lightgrey.svg)
![Framework: .NET 8.0](https://img.shields.io/badge/Framework-.NET%208.0-purple.svg)

**Triarch** é um utilitário desktop para Windows voltado para diagnóstico de hardware, monitoramento de sensores em tempo real, gerenciamento de drivers e inicialização do sistema, além de ferramentas de manutenção e testes de rede.

---

## 🚀 Funcionalidades

* **Monitoramento de Hardware em Tempo Real**: Coleta dados de sensores (temperaturas, frequências, tensões e uso de CPU/GPU).
* **Painel de Sensores & Overlay**: Interface dedicada para acompanhar o desempenho do sistema, incluindo suporte a overlay flutuante.
* **Diagnóstico de Discos (S.M.A.R.T.)**: Verificação da integridade e saúde de unidades de armazenamento (HDDs e SSDs).
* **Gerenciador de Drivers**: Identificação e listagem dos drivers instalados no sistema para controle e manutenção.
* **Gerenciador de Inicialização**: Gerenciamento dos programas que inicializam junto com o Windows para otimização de boot.
* **Testes de Rede**: Ferramentas integradas para diagnóstico e análise de conectividade.
* **Instalador de Aplicativos**: Módulo de instalação rápida de programas utilitários essenciais.
* **Temas Personalizados**: Suporte a temas visuais (Dark Mode / Light Mode) e notificações integradas nativas.

---

## 🛠️ Tecnologias Utilizadas

* **Linguagem**: C# (.NET 8.0 - Windows Desktop / WPF)
* **Monitoramento de Sensores**: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
* **Diagnóstico de Armazenamento**: DiskInfoToolkit / HidSharp
* **Compilação de Instalador**: Inno Setup

---

## 📦 Download e Instalação

A versão compilada com instalador automático pode ser obtida na aba de [Releases](https://github.com/Opazo-sys/Triarch-APP/releases):

1. Acesse a versão mais recente em **Releases**.
2. Baixe o instalador `Triarch_Setup.exe`.
3. Execute o instalador e siga as instruções na tela.

---

## 💻 Compilação a partir do Código-Fonte

Caso prefira compilar o projeto diretamente na sua máquina:

### Pré-requisitos
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) instalado.
* Windows 10 (versão 1809+) ou Windows 11.
* [Inno Setup 6](https://jrsoftware.org/isdl.php) (opcional, para empacotar o instalador).

### 💻 Como Rodar o Projeto

Se você quiser abrir o código e rodar o projeto na sua máquina:

### 1. Pré-requisitos
* Ter o [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) instalado no Windows.

### 2. Rodar em modo de desenvolvimento
Abra a pasta do projeto no terminal e execute:
```bash
dotnet run
