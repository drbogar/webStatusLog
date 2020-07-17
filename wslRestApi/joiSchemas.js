const Joi = require('joi');

exports.webStatusLogSchema = webStatusLogSchema = {
        "Timestamp": Joi.date().required(),
        "Guid": Joi.string().guid().required(),
        "Connection":
            {
                "Type": Joi.string().min(3).required(),
                "ConnectedToMac": Joi.string().allow("").max(12).required(),
            },
        "Speeds": {
            "Download": Joi.number().min(0).required(),
            "Upload": Joi.number().min(0).required(),
        }
    };