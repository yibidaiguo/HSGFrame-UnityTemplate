"""OpenAI 兼容图像接口的最小客户端：POST /images/generations 与 POST /images/edits。

请求形状照抄 `Bridges/oaiimage/src/BridgeOaiimage/ImageClient.cs`——**那份是对着真中转跑通过的**，
这里不重新发明，也不凭印象改字段名。跟着一起抄过来的还有它几条用代价换来的规矩：

* **`response_format` 一个字都不发。** gpt-image 系不认这个参数（传了报未知参数）且恒回 b64_json，
  dall-e-3 认它、默认回 url。中转背后挂什么模型不由我们决定，所以请求侧不表态、解析侧两种都吃。
* **multipart 的字段名与文件名一律自己加引号。** RFC 7578 要求带引号；宽松的解析器无所谓，
  严格的当场把整个表单判成没有 image 字段，报回来的却是「image is a required parameter」，
  指不到「引号」这两个字上。
* **下载图片 url 那一路不带任何请求头。** 图片 URL 常常指向对象存储的另一个域，
  把 Authorization 带过去等于把密钥发给第三方。
* **密钥只出现在 Authorization 头里。** 不进日志、不进异常消息，长度和前缀也不许（决策 5、78）。

只用标准库：ComfyUI 自带 torch/numpy/Pillow，但没必要为这个节点再拖一个 requests 进来。
"""

import base64
import json
import mimetypes
import os
import socket
import urllib.error
import urllib.request
import uuid


class RelayCallError(Exception):
    """一次调用失败。消息给人看，**永远不许带上密钥的任何内容**。"""


def _classify(error, timeout_seconds):
    """把 urllib 抛出来的东西翻成一句人话。分类与 oaiimage 那条链路对齐。"""
    if isinstance(error, urllib.error.HTTPError):
        status = error.code
        try:
            body = error.read().decode("utf-8", "replace")
        except Exception:  # noqa: BLE001 - 读不出错误体不该盖掉真正的状态码
            body = ""

        message = _server_message(body)
        if status in (401, 403):
            return RelayCallError(
                "中转返回 HTTP {}：密钥无效或这个账号没开通图像接口。".format(status)
            )
        if status == 429:
            return RelayCallError("中转返回 HTTP 429：被限流了，等一下再试。")
        return RelayCallError("中转返回 HTTP {}：{}".format(status, message))

    if isinstance(error, socket.timeout):
        return RelayCallError("中转超过 {} 秒没响应，这次放弃了。".format(timeout_seconds))

    if isinstance(error, urllib.error.URLError):
        reason = error.reason
        if isinstance(reason, socket.timeout):
            return RelayCallError("中转超过 {} 秒没响应，这次放弃了。".format(timeout_seconds))
        # reason 里是 DNS / 连接被拒 / TLS 这类信息，不含请求头，可以原样带出来。
        return RelayCallError("连不上中转：{}".format(reason))

    return RelayCallError("调用中转失败：{}".format(error))


def _server_message(body):
    """从错误体里抠出中转自己那句话；抠不出来就把原文截一段给人看。"""
    try:
        payload = json.loads(body)
    except (ValueError, TypeError):
        return (body or "").strip()[:400] or "（中转没给错误正文）"

    if isinstance(payload, dict):
        error = payload.get("error")
        if isinstance(error, dict) and isinstance(error.get("message"), str):
            return error["message"]
        if isinstance(payload.get("message"), str):
            return payload["message"]

    return (body or "").strip()[:400] or "（中转没给错误正文）"


def _send(url, secret, data, content_type, timeout_seconds):
    """发一次请求、读回文本。密钥只在这里出现一次。"""
    request = urllib.request.Request(url, data=data, method="POST")
    request.add_header("Authorization", "Bearer " + secret)
    if content_type:
        request.add_header("Content-Type", content_type)

    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            return response.read().decode("utf-8", "replace")
    except Exception as error:  # noqa: BLE001 - 统一翻成人话，且保证密钥不外泄
        raise _classify(error, timeout_seconds) from None


def _parse_images(response_text, endpoint_name, timeout_seconds):
    """
    解析回包的 data 数组，**b64_json 与 url 两种都吃**。
    返回 [(图片字节, 取自哪个字段)]。
    """
    try:
        payload = json.loads(response_text)
    except ValueError as error:
        raise RelayCallError("{} 回来的不是合法 JSON：{}".format(endpoint_name, error)) from None

    if not isinstance(payload, dict) or not isinstance(payload.get("data"), list):
        raise RelayCallError("{} 回来的 JSON 里没有「data」数组。".format(endpoint_name))

    images = []
    for item in payload["data"]:
        if not isinstance(item, dict):
            continue

        encoded = item.get("b64_json")
        if isinstance(encoded, str) and encoded:
            try:
                images.append((base64.b64decode(encoded), "b64_json"))
            except (ValueError, TypeError) as error:
                raise RelayCallError("{} 回来的 b64_json 解不开：{}".format(endpoint_name, error)) from None
            continue

        url = item.get("url")
        if isinstance(url, str) and url:
            images.append((_download(url, timeout_seconds), "url"))

    if not images:
        raise RelayCallError(
            "{} 回来的 data 数组里一张图都没有（既没有 b64_json 也没有 url）。".format(endpoint_name)
        )

    return images


def _download(url, timeout_seconds):
    """把图片 url 下下来。**这一路不带任何请求头**——尤其不带 Authorization。"""
    try:
        with urllib.request.urlopen(url, timeout=timeout_seconds) as response:
            return response.read()
    except Exception as error:  # noqa: BLE001
        raise RelayCallError("下载中转给的图片地址失败：{}".format(_classify(error, timeout_seconds))) from None


def generate(endpoint, secret, model_name, prompt, count, size, timeout_seconds):
    """
    文生图：POST /images/generations。

    model_name 为空串时**一个 model 参数都不发**，由中转按它自己的默认来。
    size 为空串时同理不发——各家模型的档位不一样，替它猜只会撞上「参数非法」。
    """
    body = {"prompt": prompt or "", "n": max(1, int(count))}
    if model_name:
        body["model"] = model_name
    if size:
        body["size"] = size

    response_text = _send(
        endpoint + "/images/generations",
        secret,
        json.dumps(body, ensure_ascii=False).encode("utf-8"),
        "application/json",
        timeout_seconds,
    )
    return _parse_images(response_text, "/images/generations", timeout_seconds)


def edit(endpoint, secret, model_name, prompt, count, size, image_bytes, image_name, timeout_seconds):
    """
    图生图：POST /images/edits，走 multipart/form-data，字段 image + prompt + model + n + size。
    """
    parts = [("prompt", prompt or ""), ("n", str(max(1, int(count))))]
    if model_name:
        parts.append(("model", model_name))
    if size:
        parts.append(("size", size))

    boundary = "----relay" + uuid.uuid4().hex
    data = _build_multipart(boundary, parts, image_bytes, image_name)

    response_text = _send(
        endpoint + "/images/edits",
        secret,
        data,
        "multipart/form-data; boundary=" + boundary,
        timeout_seconds,
    )
    return _parse_images(response_text, "/images/edits", timeout_seconds)


def _build_multipart(boundary, text_parts, image_bytes, image_name):
    """
    手拼 multipart 表单。**字段名与文件名一律带引号**（RFC 7578 要求，严格的解析器认死这一条）。
    图片那一段放在最后，前面全是文本段。
    """
    chunks = []

    for name, value in text_parts:
        chunks.append(("--" + boundary + "\r\n").encode("utf-8"))
        chunks.append(
            'Content-Disposition: form-data; name="{}"\r\n\r\n'.format(name).encode("utf-8")
        )
        chunks.append(str(value).encode("utf-8"))
        chunks.append(b"\r\n")

    media_type = mimetypes.guess_type(image_name)[0] or "image/png"
    chunks.append(("--" + boundary + "\r\n").encode("utf-8"))
    chunks.append(
        'Content-Disposition: form-data; name="image"; filename="{}"\r\n'.format(
            os.path.basename(image_name)
        ).encode("utf-8")
    )
    chunks.append(("Content-Type: " + media_type + "\r\n\r\n").encode("utf-8"))
    chunks.append(image_bytes)
    chunks.append(b"\r\n")

    chunks.append(("--" + boundary + "--\r\n").encode("utf-8"))
    return b"".join(chunks)
